using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Documents.Transfer
{
    // How a spreadsheet row is tied to a SEN-GEN student.
    //
    // Identifiers win outright. A SEN-GEN registration number or an official Student ID names
    // exactly one registration, so a row carrying one is matched on it and nothing else — the name
    // is only checked so a typo'd ID that lands on somebody else is caught.
    //
    // Without an ID the name has to do the work, and the office's sheets write names every way:
    // "LAST, FIRST MIDDLE", "LAST FIRST MIDDLE", "Last, First, Middle", "First Middle Last",
    // middle initials ("ANCHETA HAZEL ANN T."), accents (SEÑALISTA / SENALISTA), and the occasional
    // one-letter slip ("HAZEL ANN" for "HAZEL ANNE"). So a name is compared as a bag of words, not
    // a string: every word of the SEN-GEN last and first name must appear in the cell, and any word
    // left over must be the middle name (or its initial).
    //
    // That yields two grades. *Exact* — every word accounted for with no slips — is safe to apply
    // on its own when it is the only one. *Probable* — a one-letter slip, a missing given name, an
    // unexplained extra word — is shown to the officer for confirmation and never applied blind.
    // When several students fit equally, the program and student type columns break the tie; if
    // they cannot, the row is "ambiguous" and the officer chooses. The cost of asking is a click;
    // the cost of guessing is a paper recorded against the wrong student.

    internal enum NameMatch { None = 0, Probable = 1, Exact = 2 }

    /// <summary>A registration as the matcher sees it.</summary>
    internal sealed class MatchCandidate
    {
        public required Guid Id { get; init; }
        public required string StudentNumber { get; init; }
        public string? OfficialStudentNumber { get; init; }
        public required string LastName { get; init; }
        public required string FirstName { get; init; }
        public required string MiddleName { get; init; }
        public required ProgramTrack Program { get; init; }
        public required StudentType StudentType { get; init; }
        public string? SemesterName { get; init; }
        public bool InActiveTerm { get; init; }

        public string FullName => string.Join(' ', new[] { FirstName, MiddleName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));

        internal string[] Last = [];
        internal string[] First = [];
        internal string[] Middle = [];

        public MatchCandidate Prepare()
        {
            Last = StudentMatcher.Tokens(LastName);
            First = StudentMatcher.Tokens(FirstName);
            Middle = StudentMatcher.Tokens(MiddleName);
            return this;
        }
    }

    internal sealed class StudentMatcher
    {
        private readonly List<MatchCandidate> _all;
        private readonly Dictionary<string, MatchCandidate> _byRegistrationNumber;
        private readonly Dictionary<string, MatchCandidate> _byOfficialNumber;
        private readonly Dictionary<string, List<MatchCandidate>> _byLastToken;

        public StudentMatcher(IEnumerable<MatchCandidate> candidates)
        {
            _all = candidates.Select(c => c.Prepare()).ToList();
            _byRegistrationNumber = new(StringComparer.OrdinalIgnoreCase);
            _byOfficialNumber = new(StringComparer.OrdinalIgnoreCase);
            _byLastToken = new(StringComparer.Ordinal);
            foreach (var c in _all)
            {
                _byRegistrationNumber.TryAdd(IdKey(c.StudentNumber), c);
                if (!string.IsNullOrWhiteSpace(c.OfficialStudentNumber))
                {
                    _byOfficialNumber.TryAdd(IdKey(c.OfficialStudentNumber), c);
                }
                foreach (var token in c.Last)
                {
                    if (!_byLastToken.TryGetValue(token, out var list)) _byLastToken[token] = list = [];
                    list.Add(c);
                }
            }
        }

        public MatchCandidate? FindById(Guid id) => _all.FirstOrDefault(c => c.Id == id);

        /// <summary>
        /// The registration an identifier names: a SEN-GEN registration number or an official Student
        /// ID, whichever it is. Compared without spaces and leading zeros, because Excel drops the
        /// leading zero of a Student ID typed into a number-formatted cell.
        /// </summary>
        public MatchCandidate? FindByIdentifier(string? value, out bool viaOfficialNumber)
        {
            viaOfficialNumber = false;
            if (string.IsNullOrWhiteSpace(value)) return null;
            var key = IdKey(value);
            if (_byRegistrationNumber.TryGetValue(key, out var byReg)) return byReg;
            if (_byOfficialNumber.TryGetValue(key, out var byOfficial))
            {
                viaOfficialNumber = true;
                return byOfficial;
            }
            return null;
        }

        /// <summary>Everyone the name could be, best first, with the grade of each fit.</summary>
        public List<(MatchCandidate Candidate, NameMatch Grade)> FindByName(string[] nameTokens)
        {
            if (nameTokens.Length == 0) return [];

            // Narrow first to students sharing a word of the last name; only fall back to the whole
            // roll (for one-letter slips in the surname) when nobody does.
            var pool = nameTokens
                .Where(t => _byLastToken.ContainsKey(t))
                .SelectMany(t => _byLastToken[t])
                .Distinct()
                .ToList();
            if (pool.Count == 0) pool = _all;

            return pool
                .Select(c => (Candidate: c, Grade: Grade(c, nameTokens)))
                .Where(x => x.Grade != NameMatch.None)
                .OrderByDescending(x => x.Grade)
                .ThenByDescending(x => x.Candidate.InActiveTerm)
                .ToList();
        }

        /// <summary>
        /// How well the words of a name cell fit a registration. Order-free: the cell's words are a
        /// bag, and the registration's last, first, and middle names each draw from it.
        /// </summary>
        internal static NameMatch Grade(MatchCandidate c, string[] cellTokens)
        {
            if (c.Last.Length == 0 || c.First.Length == 0) return NameMatch.None;

            var words = cellTokens.Where(t => t.Length > 1).ToList();
            var initials = cellTokens.Where(t => t.Length == 1).Select(t => t[0]).ToList();
            var slips = 0;

            // Every word of the surname is required — it is what tells two "MARK"s apart.
            foreach (var token in c.Last)
            {
                if (!Take(words, token, ref slips)) return NameMatch.None;
            }

            // The given name: the first word is required; a later one ("ABBY *JOY*") may be missing
            // from the cell, but that costs the exact grade.
            var missingGiven = 0;
            for (var i = 0; i < c.First.Length; i++)
            {
                if (Take(words, c.First[i], ref slips)) continue;
                if (i == 0 && !TakeInitial(initials, c.First[i])) return NameMatch.None;
                missingGiven++;
            }

            // The middle name may be absent from the cell entirely; whatever is left must be it.
            foreach (var token in c.Middle)
            {
                if (Take(words, token, ref slips)) continue;
                TakeInitial(initials, token);
            }

            var unexplained = words.Count + initials.Count;
            if (slips == 0 && missingGiven == 0 && unexplained == 0) return NameMatch.Exact;

            // Too much left over means a different person who happens to share two words.
            return unexplained <= 1 && slips <= 2 ? NameMatch.Probable : NameMatch.None;
        }

        private static bool Take(List<string> words, string token, ref int slips)
        {
            var exact = words.IndexOf(token);
            if (exact >= 0)
            {
                words.RemoveAt(exact);
                return true;
            }
            var close = words.FindIndex(w => Math.Max(w.Length, token.Length) >= 4 && WithinOneEdit(w, token));
            if (close < 0) return false;
            words.RemoveAt(close);
            slips++;
            return true;
        }

        private static bool TakeInitial(List<char> initials, string token)
        {
            var i = initials.IndexOf(token[0]);
            if (i < 0) return false;
            initials.RemoveAt(i);
            return true;
        }

        /// <summary>One insertion, deletion, or substitution apart (HAZEL ANN / HAZEL ANNE, PAGUPOD / PAGUDPOD).</summary>
        internal static bool WithinOneEdit(string a, string b)
        {
            if (Math.Abs(a.Length - b.Length) > 1) return false;
            int i = 0, j = 0, edits = 0;
            while (i < a.Length && j < b.Length)
            {
                if (a[i] == b[j]) { i++; j++; continue; }
                if (++edits > 1) return false;
                if (a.Length > b.Length) i++;
                else if (b.Length > a.Length) j++;
                else { i++; j++; }
            }
            return edits + (a.Length - i) + (b.Length - j) <= 1;
        }

        /// <summary>
        /// A name's words: accents folded, upper-cased, split on punctuation and spaces. A bare number
        /// (a row counter or an ID pasted into the name cell) is not a word of anyone's name.
        /// </summary>
        internal static string[] Tokens(string? name) =>
            ChecklistColumns.Normalize(name)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Any(char.IsLetter))
                .ToArray();

        internal static string IdKey(string value)
        {
            var compact = new string(value.Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToUpperInvariant();
            var trimmed = compact.TrimStart('0');
            return trimmed.Length == 0 ? compact : trimmed;
        }
    }
}
