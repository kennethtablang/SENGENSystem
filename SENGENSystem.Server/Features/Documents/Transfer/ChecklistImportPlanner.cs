using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Documents.Transfer
{
    // The import is two calls over the same file: a preview that says, row by row, which student
    // each line was matched to and what would change, and a commit that applies the rows the officer
    // accepted. Nothing is held between them — the commit re-reads the file and re-plans against the
    // database as it is at that moment, so a checklist edited on the board in between is compared
    // against its new state, and the client can only choose *which* rows apply, never *what* they say.

    public static class MatchStatus
    {
        /// <summary>Tied by registration number / Student ID, or by an exact name that only one student has.</summary>
        public const string Matched = "Matched";
        /// <summary>A near name fit — applied only once the officer confirms it.</summary>
        public const string Probable = "Probable";
        /// <summary>More than one student fits equally; the officer chooses.</summary>
        public const string Ambiguous = "Ambiguous";
        /// <summary>The row's Student ID disagrees with the one on file, or names somebody else.</summary>
        public const string Conflict = "Conflict";
        public const string Unmatched = "Unmatched";
        /// <summary>The officer chose the student.</summary>
        public const string Manual = "Manual";
    }

    public static class CellOutcome
    {
        public const string Update = "Update";
        public const string Unchanged = "Unchanged";
        public const string KeptHigher = "KeptHigher";
        public const string NotOffered = "NotOffered";
        public const string NotRequired = "NotRequired";
        public const string Unrecognised = "Unrecognised";
        /// <summary>The status is already on file; only the date received changes.</summary>
        public const string DateUpdate = "DateUpdate";
    }

    public record ImportCandidateDto(
        Guid RegistrationId, string StudentNumber, string? OfficialStudentNumber, string FullName,
        string Program, string StudentType, string? SemesterName, string Fit);

    public record ImportChangeDto(
        string RequirementCode, string Label, string Header, string Raw,
        string? Current, string? Proposed, string Outcome, string? Note,
        // Dates received ("yyyy-MM-dd"): what SEN-GEN has, and what the file says (null when it gives none).
        string? CurrentDate = null, string? ProposedDate = null);

    public record ImportRowDto(
        string Key, string Sheet, int Row, string NameInFile, string? StudentIdInFile,
        string? RegistrationNumberInFile, string? ProgramInFile, string? StudentTypeInFile,
        string Status, string? MatchedBy, ImportCandidateDto? Student,
        IReadOnlyList<ImportCandidateDto> Candidates, IReadOnlyList<string> Notes,
        string? StudentIdToRecord, IReadOnlyList<ImportChangeDto> Changes,
        int UpdateCount, bool IncludeByDefault, IReadOnlyList<string> AlsoInRows);

    public record ImportColumnDto(string Header, string Role, string? RequirementCode, string? RequirementLabel);

    public record ImportSheetDto(
        string Name, bool Visible, bool Importable, bool Selected, int RowCount, int HeaderRow,
        IReadOnlyList<ImportColumnDto> Columns, string? Reason);

    /// <summary>What the officer sent alongside the file: which sheets, their choices, the rules.</summary>
    public sealed class ImportOptions
    {
        public string[]? Sheets { get; set; }

        /// <summary>Row key ("Sheet!12") → a registration id, a registration number, or a Student ID.</summary>
        public Dictionary<string, string>? Overrides { get; set; }

        /// <summary>Commit only: the row keys to apply.</summary>
        public string[]? Include { get; set; }

        /// <summary>Let the file move a paper backwards (an original back to a photocopy, or to not submitted).</summary>
        public bool AllowDowngrade { get; set; }

        /// <summary>Record a row's Student ID on a name-matched student who has none yet.</summary>
        public bool RecordStudentIds { get; set; } = true;
    }

    internal sealed class PlannedRow
    {
        public required ImportRowDto Dto { get; set; }
        public Guid? RegistrationId { get; init; }
        public List<(string Code, DocumentStatus Status, DateOnly? ReceivedOn)> Readings { get; } = [];
        public string? StudentIdToRecord { get; init; }
    }

    internal sealed class ImportPlan
    {
        public required List<ImportSheetDto> Sheets { get; init; }
        public required List<PlannedRow> Rows { get; init; }
        public required string Scope { get; init; }
        public bool Truncated { get; init; }
    }

    internal static class ChecklistImportPlanner
    {
        public static async Task<ImportPlan> PlanAsync(
            List<SheetTable> tables, ImportOptions options, AppDbContext db, CancellationToken ct)
        {
            var requirements = await db.AdmissionRequirements.AsNoTracking()
                .Where(r => r.IsActive)
                .OrderBy(r => r.SortOrder)
                .Select(r => new RequirementTarget(r.Code, r.Name))
                .ToListAsync(ct);
            var catalog = await DocumentChecklist.LoadCatalogAsync(db, ct);

            // Who a row may be matched to: every open registration — not rejected, in a term that is
            // not archived. The same "open" the requirement backfill uses: a closed term's records
            // stay as they were, and a rejected SIS has no checklist to keep.
            var activeSemesterId = await db.GetActiveSemesterIdAsync(ct);
            var candidates = await db.StudentRegistrations.AsNoTracking()
                .Where(r => r.Status != RegistrationStatus.Rejected && !r.Semester!.IsArchived)
                .Select(r => new MatchCandidate
                {
                    Id = r.Id,
                    StudentNumber = r.StudentNumber,
                    OfficialStudentNumber = r.OfficialStudentNumber,
                    LastName = r.LastName,
                    FirstName = r.FirstName,
                    MiddleName = r.MiddleName,
                    Program = r.Program,
                    StudentType = r.StudentType,
                    SemesterName = r.Semester!.Name,
                    InActiveTerm = r.SemesterId == activeSemesterId
                })
                .ToListAsync(ct);
            var matcher = new StudentMatcher(candidates);

            // Every official number already on file, open term or not — so recording one from the
            // sheet can never collide with the unique index on a closed term's registration.
            var takenOfficialNumbers = (await db.StudentRegistrations.AsNoTracking()
                    .Where(r => r.OfficialStudentNumber != null)
                    .Select(r => r.OfficialStudentNumber!)
                    .ToListAsync(ct))
                .Select(StudentMatcher.IdKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Sheets. Default: every visible sheet that both names students and has at least one
            // requirement column. Hidden sheets in the office workbook are empty layout copies.
            var sheetDtos = new List<ImportSheetDto>();
            var selected = new List<(SheetTable Table, List<ColumnBinding> Columns)>();
            var truncated = false;
            foreach (var table in tables)
            {
                var columns = ChecklistColumns.Bind(table.Headers, requirements);
                var hasRequirement = columns.Any(c => c.Role == ColumnRole.Requirement);
                var hasIdentity = table.Headers.Count > 0 && ChecklistColumns.NamesAStudent(table.Headers);
                var importable = hasIdentity && hasRequirement && table.Rows.Count > 0;
                var reason = !hasIdentity ? "No student name or ID column was found."
                    : !hasRequirement ? "No column matches an admission requirement."
                    : table.Rows.Count == 0 ? "The sheet has no rows below its header."
                    : null;
                var isSelected = importable && (options.Sheets is { } chosen
                    ? chosen.Contains(table.Name, StringComparer.Ordinal)
                    : table.IsVisible);
                if (table.Rows.Count > ChecklistWorkbookReader.MaxDataRows) truncated = true;

                sheetDtos.Add(new ImportSheetDto(
                    table.Name, table.IsVisible, importable, isSelected,
                    Math.Min(table.Rows.Count, ChecklistWorkbookReader.MaxDataRows), table.HeaderRowNumber,
                    columns.Select(c => new ImportColumnDto(
                        c.Header, c.Role.ToString(), c.RequirementCode,
                        c.RequirementCode is { } code ? catalog.Label(code) : null)).ToList(),
                    reason));
                if (isSelected) selected.Add((table, columns));
            }

            // Current checklists of everyone the file could touch — loaded once.
            var documents = await db.RegistrationDocuments.AsNoTracking()
                .Where(d => d.StudentRegistration!.Status != RegistrationStatus.Rejected
                    && !d.StudentRegistration.Semester!.IsArchived)
                .Select(d => new { d.StudentRegistrationId, d.RequirementCode, d.Status, d.SubmittedOn })
                .ToListAsync(ct);
            var current = documents
                .GroupBy(d => d.StudentRegistrationId)
                .ToDictionary(g => g.Key, g => g.ToDictionary(
                    d => d.RequirementCode, d => (d.Status, d.SubmittedOn), StringComparer.OrdinalIgnoreCase));
            var today = DocumentChecklist.Today();

            var overrides = options.Overrides ?? [];
            var rows = new List<PlannedRow>();

            foreach (var (table, columns) in selected)
            {
                string Value(SheetRow row, ColumnRole role) =>
                    columns.FirstOrDefault(c => c.Role == role) is { } col ? row.At(col.Index).Text.Trim() : string.Empty;

                foreach (var row in table.Rows.Take(ChecklistWorkbookReader.MaxDataRows))
                {
                    var key = $"{table.Name}!{row.Number}";
                    var regNo = Value(row, ColumnRole.RegistrationNumber);
                    var studentId = Value(row, ColumnRole.StudentId);
                    var fullName = Value(row, ColumnRole.FullName);
                    var structured = string.Join(' ', new[]
                    {
                        Value(row, ColumnRole.LastName), Value(row, ColumnRole.FirstName), Value(row, ColumnRole.MiddleName)
                    }.Where(s => s.Length > 0));
                    var nameInFile = structured.Length > 0 ? structured : fullName;
                    var programTexts = columns.Where(c => c.Role == ColumnRole.Program)
                        .Select(c => row.At(c.Index).Text.Trim()).Where(t => t.Length > 0).ToList();
                    var programHints = programTexts.Select(ChecklistColumns.ParseProgram).OfType<ProgramTrack>().Distinct().ToList();
                    var typeText = Value(row, ColumnRole.StudentType);
                    var typeHint = ChecklistColumns.ParseStudentType(typeText);

                    // A row with no name and no ID is a stray note under the table, not a student.
                    if (nameInFile.Length == 0 && regNo.Length == 0 && studentId.Length == 0) continue;

                    var resolution = Resolve(
                        matcher, nameInFile, regNo, studentId, programHints, typeHint,
                        overrides.GetValueOrDefault(key), takenOfficialNumbers);

                    var notes = new List<string>(resolution.Notes);
                    var target = resolution.Student;
                    if (target is not null && programHints.Count > 0 && !programHints.Contains(target.Program))
                    {
                        notes.Add($"The file says {string.Join(" / ", programHints)}; SEN-GEN has {target.Program}.");
                    }

                    // Cell by cell, against the matched student's checklist as it stands.
                    var changes = new List<ImportChangeDto>();
                    var readings = new List<(string, DocumentStatus, DateOnly?)>();
                    var checklist = target is not null ? current.GetValueOrDefault(target.Id) : null;
                    foreach (var col in columns.Where(c => c.Role == ColumnRole.Requirement))
                    {
                        var code = col.RequirementCode!;
                        var cell = row.At(col.Index);
                        var reading = ChecklistCellReader.Read(cell);
                        if (reading.Meaning is CellMeaning.Blank or CellMeaning.NotApplicable) continue;

                        var label = catalog.Label(code);

                        // The date received: its own column when the sheet has one (SEN-GEN's export
                        // does), else whatever date was written in the status cell ("✅ 9/23/2026").
                        var dateColumn = columns.FirstOrDefault(c =>
                            c.Role == ColumnRole.RequirementDate && string.Equals(c.RequirementCode, code, StringComparison.OrdinalIgnoreCase));
                        var receivedOn = (dateColumn is not null ? ChecklistCellReader.ParseDate(row.At(dateColumn.Index).Text) : null)
                            ?? reading.ReceivedOn;
                        string? dateNote = null;
                        if (receivedOn is { } d && (d > today || d.Year < 2000))
                        {
                            dateNote = $"The date {d:MMM d, yyyy} is {(d > today ? "in the future" : "mistyped")} — not used.";
                            receivedOn = null;
                        }
                        if (reading.Status == DocumentStatus.NotSubmitted) receivedOn = null;
                        var proposedDate = receivedOn?.ToString("yyyy-MM-dd");
                        if (reading.Meaning == CellMeaning.Unrecognised)
                        {
                            changes.Add(new(code, label, col.Header, cell.Text, null, null, CellOutcome.Unrecognised,
                                "Not a status SEN-GEN can read — left as it is."));
                            continue;
                        }

                        var proposed = reading.Status!.Value;
                        if (target is null)
                        {
                            changes.Add(new(code, label, col.Header, cell.Text, null, proposed.ToString(), CellOutcome.Update,
                                Join(reading.Note, dateNote), null, proposedDate));
                            continue;
                        }

                        if (!catalog.AppliesTo(code, target.StudentType, target.Program))
                        {
                            changes.Add(new(code, label, col.Header, cell.Text, null, proposed.ToString(), CellOutcome.NotRequired,
                                $"Not asked of a {Describe(target.StudentType)} in {target.Program}."));
                            continue;
                        }
                        if (!catalog.StatusesFor(code).Contains(proposed))
                        {
                            changes.Add(new(code, label, col.Header, cell.Text, null, proposed.ToString(), CellOutcome.NotOffered,
                                $"{label} cannot be recorded as {ChecklistCellReader.ExportLabel(proposed).ToLowerInvariant()}."));
                            continue;
                        }

                        var (now, nowDate) = checklist is not null && checklist.TryGetValue(code, out var s)
                            ? s : (DocumentStatus.NotSubmitted, (DateOnly?)null);
                        var outcome = now == proposed
                            ? (receivedOn is not null && receivedOn != nowDate ? CellOutcome.DateUpdate : CellOutcome.Unchanged)
                            : !options.AllowDowngrade && ChecklistCellReader.Rank(proposed) < ChecklistCellReader.Rank(now)
                                ? CellOutcome.KeptHigher
                                : CellOutcome.Update;
                        var note = outcome switch
                        {
                            CellOutcome.KeptHigher => $"SEN-GEN already has {ChecklistCellReader.ExportLabel(now).ToLowerInvariant()} — kept.",
                            CellOutcome.Update when proposed != DocumentStatus.NotSubmitted && receivedOn is null =>
                                Join(reading.Note, dateNote, "No date in the file — today's date will be recorded."),
                            _ => Join(reading.Note, dateNote)
                        };
                        changes.Add(new(code, label, col.Header, cell.Text, now.ToString(), proposed.ToString(), outcome, note,
                            nowDate?.ToString("yyyy-MM-dd"), proposedDate));
                        readings.Add((code, proposed, receivedOn));
                    }

                    var includeByDefault = target is not null
                        && resolution.Status is MatchStatus.Matched or MatchStatus.Manual;
                    var dto = new ImportRowDto(
                        key, table.Name, row.Number,
                        nameInFile, NullIfEmpty(studentId), NullIfEmpty(regNo),
                        programTexts.Count > 0 ? string.Join(" / ", programTexts.Distinct()) : null,
                        NullIfEmpty(typeText),
                        resolution.Status, resolution.MatchedBy,
                        target is null ? null : ToDto(target, resolution.Fit),
                        resolution.Candidates.Select(c => ToDto(c.Candidate, FitLabel(c.Grade))).ToList(),
                        notes, resolution.StudentIdToRecord, changes,
                        changes.Count(c => c.Outcome is CellOutcome.Update or CellOutcome.DateUpdate), includeByDefault, []);

                    var planned = new PlannedRow { Dto = dto, RegistrationId = target?.Id, StudentIdToRecord = resolution.StudentIdToRecord };
                    planned.Readings.AddRange(readings);
                    rows.Add(planned);
                }
            }

            // The same student on two rows — once on "Admission Requirements" and again on their
            // program's sheet, in the office workbook. Both are kept; the commit takes the furthest
            // status each paper reached across them. Said on each row so it is not a surprise.
            foreach (var group in rows.Where(r => r.RegistrationId is not null)
                         .GroupBy(r => r.RegistrationId!.Value)
                         .Where(g => g.Count() > 1))
            {
                foreach (var r in group)
                {
                    r.Dto = r.Dto with { AlsoInRows = group.Where(o => o != r).Select(o => o.Dto.Key).ToList() };
                }
            }

            var scope = activeSemesterId is null
                ? "Students are matched against every open registration (no term is active)."
                : "Students are matched against every open registration — any term not archived, rejected SIS excluded.";
            return new ImportPlan
            {
                Sheets = sheetDtos,
                Rows = rows,
                Scope = scope,
                Truncated = truncated
            };
        }

        private sealed record Resolution(
            string Status, string? MatchedBy, MatchCandidate? Student, string Fit,
            List<(MatchCandidate Candidate, NameMatch Grade)> Candidates, List<string> Notes, string? StudentIdToRecord);

        private static Resolution Resolve(
            StudentMatcher matcher, string nameInFile, string regNo, string studentId,
            List<ProgramTrack> programHints, StudentType? typeHint, string? manual,
            HashSet<string> takenOfficialNumbers)
        {
            var notes = new List<string>();
            var tokens = StudentMatcher.Tokens(nameInFile);
            var byName = matcher.FindByName(tokens);

            // 1. The officer's own choice — a candidate picked from the list, or a number typed in.
            if (!string.IsNullOrWhiteSpace(manual))
            {
                var chosen = Guid.TryParse(manual, out var id)
                    ? matcher.FindById(id)
                    : matcher.FindByIdentifier(manual, out _);
                if (chosen is null)
                {
                    notes.Add($"\"{manual}\" does not match an open registration.");
                }
                else
                {
                    return new(MatchStatus.Manual, "Chosen by you", chosen, "Chosen", byName, notes,
                        StudentIdToRecordFor(chosen, studentId, takenOfficialNumbers, notes));
                }
            }

            // 2. An identifier names one registration.
            foreach (var (value, label) in new[] { (regNo, "Registration no."), (studentId, "Student ID") })
            {
                var hit = matcher.FindByIdentifier(value, out var viaOfficial);
                if (hit is null) continue;

                var grade = tokens.Length == 0 ? NameMatch.Exact : StudentMatcher.Grade(hit, tokens);
                var matchedBy = viaOfficial ? "Student ID" : "Registration no.";
                if (grade == NameMatch.None)
                {
                    notes.Add($"{label} {value} is {hit.FullName} in SEN-GEN, but the row names \"{nameInFile}\".");
                    return new(MatchStatus.Conflict, matchedBy, hit, "ID only", [(hit, NameMatch.None), .. byName.Where(c => c.Candidate.Id != hit.Id)],
                        notes, null);
                }
                return new(MatchStatus.Matched, matchedBy, hit, FitLabel(grade), [], notes,
                    StudentIdToRecordFor(hit, studentId, takenOfficialNumbers, notes));
            }

            // 3. The name — exact fits first, then near ones, each narrowed by program and type.
            var exact = Narrow(byName.Where(c => c.Grade == NameMatch.Exact).ToList(), programHints, typeHint);
            var probable = Narrow(byName.Where(c => c.Grade == NameMatch.Probable).ToList(), programHints, typeHint);
            var shortlist = byName.Take(5).ToList();

            if (studentId.Length > 0)
            {
                notes.Add($"Student ID {studentId} is not on file in SEN-GEN yet.");
            }

            if (exact.Count == 1)
            {
                var hit = exact[0].Candidate;
                return Checked(hit, byName.Count(c => c.Grade == NameMatch.Exact) > 1 ? "Name + program" : "Name",
                    MatchStatus.Matched, "Exact name");
            }
            if (exact.Count > 1)
            {
                notes.Add($"{exact.Count} students share this exact name — choose the right one.");
                return new(MatchStatus.Ambiguous, null, null, string.Empty, exact.Take(5).ToList(), notes, null);
            }
            if (probable.Count == 1)
            {
                return Checked(probable[0].Candidate, "Similar name", MatchStatus.Probable, "Similar name");
            }
            if (probable.Count > 1)
            {
                notes.Add("Several students have a similar name — choose the right one.");
                return new(MatchStatus.Ambiguous, null, null, string.Empty, probable.Take(5).ToList(), notes, null);
            }

            notes.Add(tokens.Length == 0
                ? "The row has no usable name or ID."
                : "No open registration matches this name. Type the student's registration no. or Student ID to link it.");
            return new(MatchStatus.Unmatched, null, null, string.Empty, shortlist, notes, null);

            // A name match with a Student ID in the row: the ID on file must not contradict it.
            Resolution Checked(MatchCandidate hit, string matchedBy, string status, string fit)
            {
                if (studentId.Length > 0 && !string.IsNullOrWhiteSpace(hit.OfficialStudentNumber)
                    && StudentMatcher.IdKey(hit.OfficialStudentNumber) != StudentMatcher.IdKey(studentId))
                {
                    notes.Clear();
                    notes.Add($"The name fits {hit.FullName}, but their Student ID on file is {hit.OfficialStudentNumber}, not {studentId}.");
                    return new(MatchStatus.Conflict, matchedBy, hit, fit, shortlist, notes, null);
                }
                return new(status, matchedBy, hit, fit, status == MatchStatus.Matched ? [] : shortlist, notes,
                    StudentIdToRecordFor(hit, studentId, takenOfficialNumbers, notes));
            }
        }

        /// <summary>
        /// Program and student type break a tie — but never empty the list. A sheet that files a
        /// student under the wrong program (the office workbook has a few) still finds them.
        /// </summary>
        private static List<(MatchCandidate Candidate, NameMatch Grade)> Narrow(
            List<(MatchCandidate Candidate, NameMatch Grade)> list, List<ProgramTrack> programs, StudentType? type)
        {
            if (list.Count <= 1) return list;
            if (programs.Count > 0)
            {
                var byProgram = list.Where(c => programs.Contains(c.Candidate.Program)).ToList();
                if (byProgram.Count > 0) list = byProgram;
            }
            if (list.Count > 1 && type is { } t)
            {
                var byType = list.Where(c => c.Candidate.StudentType == t).ToList();
                if (byType.Count > 0) list = byType;
            }
            if (list.Count > 1)
            {
                var current = list.Where(c => c.Candidate.InActiveTerm).ToList();
                if (current.Count == 1) list = current;
            }
            return list;
        }

        /// <summary>
        /// The Student ID a name-matched row would record on the student: only when they have none
        /// yet and nobody else holds it. This is what makes the next import of the same sheet exact.
        /// </summary>
        private static string? StudentIdToRecordFor(
            MatchCandidate student, string studentId, HashSet<string> taken, List<string> notes)
        {
            if (studentId.Length == 0 || !string.IsNullOrWhiteSpace(student.OfficialStudentNumber)) return null;
            if (studentId.Length > 30)
            {
                notes.Add("The Student ID in the row is too long to record (30 characters max).");
                return null;
            }
            if (taken.Contains(StudentMatcher.IdKey(studentId)))
            {
                notes.Add($"Student ID {studentId} is already recorded on another registration — not copied.");
                return null;
            }
            return studentId;
        }

        private static ImportCandidateDto ToDto(MatchCandidate c, string fit) =>
            new(c.Id, c.StudentNumber, c.OfficialStudentNumber, c.FullName,
                c.Program.ToString(), c.StudentType.ToString(), c.SemesterName, fit);

        private static string FitLabel(NameMatch grade) => grade switch
        {
            NameMatch.Exact => "Exact name",
            NameMatch.Probable => "Similar name",
            _ => "Name differs"
        };

        private static string Describe(StudentType type) =>
            type == StudentType.Transferee ? "transferee" : "new student";

        private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

        private static string? Join(params string?[] parts)
        {
            var present = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            return present.Count == 0 ? null : string.Join(" ", present);
        }
    }
}
