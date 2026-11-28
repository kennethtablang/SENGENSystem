using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Documents.Transfer
{
    // Reading an admissions-office spreadsheet into something the import can reason about.
    //
    // The office's real workbook ("Student Admission 2026-27") is the design input here, and it is
    // not one layout but several: the "Admission Requirements" sheet names a student in one free-text
    // cell ("ALFEREZ, JOSHUA SEÑALISTA", "ANCHETA HAZEL ANN T.", "Balatico, Rowelinda,Cabarlo") with
    // no ID at all, while the per-program sheets (HRS, HRA 1, ITP, Transferees) carry the official
    // STUDENT ID and split LAST / FIRST / MIDDLE NAME columns. Requirement headers vary too
    // ("Hepa A", "HEPA A (HBsAg)", "FORM 138", "Original Form 138/SF9-SHS (Progress Report Card)"),
    // and so do the cell values ("✅ Submitted", "✅ 9/23/2026", "xerox", "Copy", "transferee",
    // "no req"). So headers are recognised by role, and cells by meaning, rather than by an exact
    // template — and SEN-GEN's own export reads back through the same rules.

    /// <summary>One cell as text, plus whether Excel held it as a real date.</summary>
    internal readonly record struct SheetCell(string Text, bool IsDate);

    internal sealed record SheetRow(int Number, IReadOnlyList<SheetCell> Cells)
    {
        public SheetCell At(int column) =>
            column >= 0 && column < Cells.Count ? Cells[column] : new SheetCell(string.Empty, false);
    }

    /// <summary>A sheet (or a CSV file) as a header row and the data rows below it.</summary>
    internal sealed record SheetTable(
        string Name, bool IsVisible, int HeaderRowNumber, IReadOnlyList<string> Headers, IReadOnlyList<SheetRow> Rows);

    internal static class ChecklistWorkbookReader
    {
        public const long MaxFileBytes = 5 * 1024 * 1024;
        public const int MaxDataRows = 5000;

        /// <summary>How far down a sheet the header row is looked for (titles sometimes sit above it).</summary>
        private const int HeaderSearchDepth = 10;

        public static bool IsCsv(string fileName) => fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

        public static bool IsSupported(string fileName) =>
            IsCsv(fileName) || fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Every sheet in the file. For each, the header row is the first of the top rows that names
        /// a student (a name or ID column) — so a title line above the table does not break it.
        /// </summary>
        public static List<SheetTable> Read(Stream stream, string fileName)
        {
            var raw = IsCsv(fileName)
                ? [("CSV", true, ReadCsv(stream))]
                : ReadXlsx(stream);

            var tables = new List<SheetTable>();
            foreach (var (name, visible, rows) in raw)
            {
                var headerIndex = -1;
                for (var i = 0; i < Math.Min(rows.Count, HeaderSearchDepth); i++)
                {
                    var headers = rows[i].Cells.Select(c => c.Text).ToList();
                    if (ChecklistColumns.NamesAStudent(headers))
                    {
                        headerIndex = i;
                        break;
                    }
                }
                if (headerIndex < 0)
                {
                    tables.Add(new SheetTable(name, visible, 0, [], []));
                    continue;
                }

                var header = rows[headerIndex];
                var data = rows.Skip(headerIndex + 1)
                    .Where(r => r.Cells.Any(c => c.Text.Length > 0))
                    .Take(MaxDataRows + 1)
                    .ToList();
                tables.Add(new SheetTable(
                    name, visible, header.Number, header.Cells.Select(c => c.Text).ToList(), data));
            }
            return tables;
        }

        private static List<(string Name, bool Visible, List<SheetRow> Rows)> ReadXlsx(Stream stream)
        {
            using var workbook = new XLWorkbook(stream);
            var sheets = new List<(string, bool, List<SheetRow>)>();
            foreach (var sheet in workbook.Worksheets)
            {
                var rows = new List<SheetRow>();
                var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
                var lastRow = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? 0, MaxDataRows + HeaderSearchDepth + 1);
                for (var r = 1; r <= lastRow; r++)
                {
                    var row = sheet.Row(r);
                    var cells = new SheetCell[lastColumn];
                    for (var c = 1; c <= lastColumn; c++)
                    {
                        cells[c - 1] = ReadCell(row.Cell(c));
                    }
                    rows.Add(new SheetRow(r, cells));
                }
                sheets.Add((sheet.Name, sheet.Visibility == XLWorksheetVisibility.Visible, rows));
            }
            return sheets;
        }

        private static SheetCell ReadCell(IXLCell cell)
        {
            try
            {
                switch (cell.DataType)
                {
                    case XLDataType.Blank:
                        return new SheetCell(string.Empty, false);
                    case XLDataType.DateTime:
                        return new SheetCell(
                            cell.GetDateTime().ToString("M/d/yyyy", CultureInfo.InvariantCulture), true);
                    case XLDataType.Number:
                        // Formatted, so a Student ID stored as a number under a "00000000000" format
                        // keeps its leading zero (02000494658 rather than 2000494658).
                        return new SheetCell(cell.GetFormattedString().Trim(), false);
                    default:
                        return new SheetCell(cell.GetString().Trim(), false);
                }
            }
            catch (Exception)
            {
                // A broken number format or a formula error in one cell must not sink the file.
                return new SheetCell(cell.Value.ToString()?.Trim() ?? string.Empty, false);
            }
        }

        /// <summary>
        /// A small RFC 4180 reader: quoted fields, doubled quotes, newlines inside quotes. The
        /// delimiter is sniffed from the first line (Excel in a Filipino locale saves with commas,
        /// but a semicolon or tab export should not need re-saving). Text is UTF-8, falling back to
        /// Latin-1 so a file saved as "CSV" (ANSI) from older Excel still reads its Ñ.
        /// </summary>
        private static List<SheetRow> ReadCsv(Stream stream)
        {
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            string text;
            try
            {
                text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                text = Encoding.Latin1.GetString(bytes);
            }
            if (text.Length > 0 && text[0] == '﻿') text = text[1..];

            var firstLine = text.Split('\n', 2)[0];
            var delimiter = new[] { ',', ';', '\t' }
                .OrderByDescending(d => firstLine.Count(ch => ch == d))
                .First();

            var rows = new List<SheetRow>();
            var fields = new List<SheetCell>();
            var field = new StringBuilder();
            var inQuotes = false;
            var rowNumber = 1;

            void EndField()
            {
                fields.Add(new SheetCell(field.ToString().Trim(), false));
                field.Clear();
            }
            void EndRow()
            {
                EndField();
                rows.Add(new SheetRow(rowNumber++, fields.ToArray()));
                fields.Clear();
            }

            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else field.Append(ch);
                }
                else if (ch == '"') inQuotes = true;
                else if (ch == delimiter) EndField();
                else if (ch == '\r') { /* handled with the \n */ }
                else if (ch == '\n') EndRow();
                else field.Append(ch);

                if (rows.Count > MaxDataRows + HeaderSearchDepth + 1) break;
            }
            if (field.Length > 0 || fields.Count > 0) EndRow();
            return rows;
        }
    }

    // ---------------- Columns ----------------

    internal enum ColumnRole
    {
        Ignored,
        RegistrationNumber,
        StudentId,
        FullName,
        LastName,
        FirstName,
        MiddleName,
        Program,
        StudentType,
        Requirement,
        /// <summary>The date a requirement was received ("Form 138 — Date received").</summary>
        RequirementDate
    }

    /// <summary>What one header was read as. <paramref name="RequirementCode"/> is set for a requirement column.</summary>
    internal sealed record ColumnBinding(int Index, string Header, ColumnRole Role, string? RequirementCode = null);

    /// <summary>A requirement the import may write: its code, label, and the statuses it offers.</summary>
    internal sealed record RequirementTarget(string Code, string Name);

    internal static class ChecklistColumns
    {
        /// <summary>
        /// Alternative header wordings for the nine built-in requirements, as the office's sheets
        /// actually write them. Matched on whole words of the normalized header, so "FORM 138" and
        /// "Original Form 138/SF9-SHS (Progress Report Card)" both land on the report card, while
        /// "Certificates" (a remarks column on the per-program sheets) lands on nothing.
        /// </summary>
        private static readonly (string Code, string[] Aliases)[] BuiltInAliases =
        [
            ("HepaA", ["HEPA A", "HEPATITIS A"]),
            ("HepaB", ["HEPA B", "HEPATITIS B"]),
            ("Xray", ["XRAY", "X RAY", "CHEST X RAY"]),
            ("Form138_SF9", ["138", "SF9", "REPORT CARD"]),
            ("Form137_SF10", ["137", "SF10", "PERMANENT RECORD"]),
            ("HonorableDismissal", ["HONORABLE DISMISSAL", "CERTIFICATE OF TRANSFER"]),
            ("OfficialTranscript", ["TRANSCRIPT", "TOR", "OTR"]),
            ("GoodMoral", ["GOOD MORAL"]),
            ("PsaBirthCertificate", ["PSA", "BIRTH CERTIFICATE"])
        ];

        /// <summary>Whether a row of headers identifies students at all (a name or an ID column).</summary>
        public static bool NamesAStudent(IReadOnlyList<string> headers)
        {
            var roles = headers.Select(h => IdentityRole(Normalize(h))).ToHashSet();
            return roles.Contains(ColumnRole.FullName)
                || roles.Contains(ColumnRole.StudentId)
                || roles.Contains(ColumnRole.RegistrationNumber)
                || (roles.Contains(ColumnRole.LastName) && roles.Contains(ColumnRole.FirstName));
        }

        /// <summary>
        /// Binds every header to a role. A requirement that two columns both claim keeps the first;
        /// the second is reported as ignored rather than letting one overwrite the other unseen.
        /// </summary>
        public static List<ColumnBinding> Bind(IReadOnlyList<string> headers, IReadOnlyList<RequirementTarget> requirements)
        {
            var bindings = new List<ColumnBinding>();
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var datesClaimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var identityTaken = new HashSet<ColumnRole>();

            for (var i = 0; i < headers.Count; i++)
            {
                var header = headers[i].Trim();
                var norm = Normalize(header);
                if (norm.Length == 0) continue;

                // "<requirement> — Date received": the export writes one beside every paper.
                if (DateColumnPrefix(norm) is { } prefix
                    && RequirementFor(prefix, requirements) is { } dateCode)
                {
                    bindings.Add(datesClaimed.Add(dateCode)
                        ? new ColumnBinding(i, header, ColumnRole.RequirementDate, dateCode)
                        : new ColumnBinding(i, header, ColumnRole.Ignored));
                    continue;
                }

                var role = IdentityRole(norm);
                if (role != ColumnRole.Ignored)
                {
                    // Program hints may come from more than one column (the office sheet has both a
                    // chosen program and a "Program/Course"); every other identity role keeps its first.
                    if (role == ColumnRole.Program || identityTaken.Add(role))
                    {
                        bindings.Add(new ColumnBinding(i, header, role));
                    }
                    else
                    {
                        bindings.Add(new ColumnBinding(i, header, ColumnRole.Ignored));
                    }
                    continue;
                }

                var code = RequirementFor(norm, requirements);
                if (code is not null && claimed.Add(code))
                {
                    bindings.Add(new ColumnBinding(i, header, ColumnRole.Requirement, code));
                }
                else
                {
                    bindings.Add(new ColumnBinding(i, header, ColumnRole.Ignored));
                }
            }
            return bindings;
        }

        private static readonly string[] DateSuffixes =
            [" DATE RECEIVED", " DATE SUBMITTED", " RECEIVED ON", " SUBMITTED ON", " RECEIVED", " DATE"];

        /// <summary>The requirement part of a date header, or null when the header is not one.</summary>
        private static string? DateColumnPrefix(string norm)
        {
            foreach (var suffix in DateSuffixes)
            {
                if (norm.EndsWith(suffix) && norm.Length > suffix.Length)
                {
                    return norm[..^suffix.Length].Trim();
                }
            }
            return null;
        }

        private static ColumnRole IdentityRole(string norm)
        {
            if (norm.Length == 0) return ColumnRole.Ignored;
            var padded = $" {norm} ";

            if (padded.Contains(" REGISTRATION ") || padded.Contains(" REG NO ") || norm == "REFERENCE NO"
                || norm == "STUDENTNUMBER")
                return ColumnRole.RegistrationNumber;
            if (norm is "STUDENT ID" or "STUDENT NO" or "STUDENT NUMBER" or "ID NUMBER" or "ID NO" or "ID"
                || padded.Contains(" OFFICIAL STUDENT ") || norm == "STUDENTID")
                return ColumnRole.StudentId;
            if (norm is "LAST NAME" or "SURNAME" or "FAMILY NAME" or "LASTNAME") return ColumnRole.LastName;
            if (norm is "FIRST NAME" or "GIVEN NAME" or "FIRSTNAME") return ColumnRole.FirstName;
            if (norm is "MIDDLE NAME" or "MIDDLE" or "MI" or "MIDDLENAME" or "MIDDLE INITIAL") return ColumnRole.MiddleName;
            if (norm is "NAME" or "FULL NAME" or "STUDENT NAME" or "STUDENT S NAME" or "STUDENTS NAME"
                or "NAME OF STUDENT" or "FULLNAME")
                return ColumnRole.FullName;
            if (padded.Contains(" TRANSFEREE ") || norm is "STUDENT TYPE" or "TYPE" or "STUDENTTYPE")
                return ColumnRole.StudentType;
            if (padded.Contains(" PROGRAM ") || padded.Contains(" COURSE ") || norm == "CHOSEN PROGRAM")
                return ColumnRole.Program;
            return ColumnRole.Ignored;
        }

        private static string? RequirementFor(string norm, IReadOnlyList<RequirementTarget> requirements)
        {
            // The catalog's own names and codes first — SEN-GEN's export writes the name as the
            // header, and a requirement added on the Manage Requirements screen has no alias.
            foreach (var r in requirements)
            {
                if (Normalize(r.Name) == norm || Normalize(r.Code) == norm || Normalize(Humanized(r.Code)) == norm)
                {
                    return r.Code;
                }
            }

            var padded = $" {norm} ";
            foreach (var (code, aliases) in BuiltInAliases)
            {
                if (!requirements.Any(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase))) continue;
                if (aliases.Any(a => padded.Contains($" {a} ")))
                {
                    return requirements.First(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase)).Code;
                }
            }
            return null;
        }

        private static string Humanized(string code) => Regex.Replace(code, "([a-z0-9])([A-Z])", "$1 $2");

        /// <summary>Upper-case, accents folded (Ñ → N), punctuation to spaces, whitespace collapsed.</summary>
        public static string Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var folded = FoldAccents(text).ToUpperInvariant();
            var sb = new StringBuilder(folded.Length);
            foreach (var ch in folded)
            {
                sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            }
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        public static string FoldAccents(string text)
        {
            var decomposed = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var ch in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>Reads ITP / HRS / HRA out of a program cell such as "HRA (Hotel and Restaurant Administration)".</summary>
        public static ProgramTrack? ParseProgram(string? text)
        {
            var padded = $" {Normalize(text)} ";
            foreach (var program in Enum.GetValues<ProgramTrack>())
            {
                if (padded.Contains($" {program.ToString().ToUpperInvariant()} ")) return program;
            }
            if (padded.Contains(" INFORMATION TECHNOLOGY ")) return ProgramTrack.ITP;
            if (padded.Contains(" HOTEL ")) return ProgramTrack.HRA;
            if (padded.Contains(" HOSPITALITY ")) return ProgramTrack.HRS;
            return null;
        }

        /// <summary>"New Student" / "Transferee" (and the sheet's typos, e.g. "New Stident", "tranferee").</summary>
        public static StudentType? ParseStudentType(string? text)
        {
            var norm = Normalize(text);
            if (norm.Length == 0) return null;
            if (norm.StartsWith("TRAN")) return StudentType.Transferee;
            if (norm.StartsWith("NEW") || norm.StartsWith("FRESH")) return StudentType.NewStudent;
            return null;
        }
    }

    // ---------------- Cell values ----------------

    internal enum CellMeaning
    {
        /// <summary>Nothing recorded — leaves the checklist as it is.</summary>
        Blank,
        /// <summary>A status the checklist can record.</summary>
        Status,
        /// <summary>"transferee", "exempt" — the office marking the paper as not asked of this student.</summary>
        NotApplicable,
        /// <summary>Text the import cannot read as a status; shown, never guessed at.</summary>
        Unrecognised
    }

    /// <summary>
    /// What one requirement cell says. <paramref name="ReceivedOn"/> is the date written with it
    /// ("✅ 9/23/2026", or a real date cell) — the day the office logged the paper.
    /// </summary>
    internal readonly record struct CellReading(
        CellMeaning Meaning, DocumentStatus? Status, string? Note = null, DateOnly? ReceivedOn = null);

    internal static class ChecklistCellReader
    {
        private static readonly Regex DateLike = new(@"\b\d{1,4}[/-]\d{1,2}[/-]\d{1,4}\b", RegexOptions.Compiled);
        private static readonly char[] CheckMarks = ['✅', '✔', '☑', '✓', '√'];

        public static CellReading Read(SheetCell cell)
        {
            var raw = cell.Text.Trim();
            if (raw.Length == 0) return new(CellMeaning.Blank, null);

            var hasCheck = raw.IndexOfAny(CheckMarks) >= 0;
            var text = Regex.Replace(ChecklistColumns.FoldAccents(raw).ToLowerInvariant(), @"[✅✔☑✓√]", " ");
            text = Regex.Replace(text, @"\s+", " ").Trim();
            var bare = Regex.Replace(text, @"[^a-z0-9/ -]", "").Trim();

            if (bare.Length == 0 && !hasCheck) return new(CellMeaning.Blank, null);
            if (bare is "-" or "--" or "none" or "n/a" or "na" or "n a" or "null") return new(CellMeaning.Blank, null);

            // Order matters: "not submitted" contains "submitted", "no xerox" contains "xerox".
            if (bare.StartsWith("not submitted") || bare.StartsWith("no ") || bare is "no" or "missing"
                || bare.Contains("not in file") || bare.Contains("to follow") || bare.StartsWith("pending")
                || bare.StartsWith("lacking"))
                return new(CellMeaning.Status, DocumentStatus.NotSubmitted);

            if (bare.StartsWith("transfer") || bare.StartsWith("tranferee") || bare is "not applicable" or "exempt")
                return new(CellMeaning.NotApplicable, null, raw);

            var dateText = DateLike.Match(text);
            var date = dateText.Success ? ParseDate(dateText.Value) : null;

            if (bare.Contains("certificate of grades") || bare is "cog" or "c o g")
                return new(CellMeaning.Status, DocumentStatus.CertificateOfGrades, null, date);

            if (bare.Contains("xerox") || bare.Contains("photocopy") || bare.Contains("copy") || bare is "ctc")
                return new(CellMeaning.Status, DocumentStatus.XeroxCopy, null, date);

            if (hasCheck || bare.StartsWith("submitted") || bare.StartsWith("original") || bare.StartsWith("received")
                || bare is "yes" or "y" or "ok" or "complete" or "done" or "on file")
            {
                return new(CellMeaning.Status, DocumentStatus.Submitted, null, date);
            }

            // A bare date in a requirement column is how the per-program sheets note when a paper came in.
            if (cell.IsDate || dateText.Success && dateText.Value.Length >= bare.Length - 1)
            {
                return new(CellMeaning.Status, DocumentStatus.Submitted, "only a date in the cell — read as received", ParseDate(cell.Text));
            }

            return new(CellMeaning.Unrecognised, null, raw);
        }

        private static readonly string[] DateFormats =
        [
            "M/d/yyyy", "M/d/yy", "M-d-yyyy", "M-d-yy", "yyyy-M-d", "yyyy/M/d",
            "MMM d, yyyy", "MMMM d, yyyy", "MMM d yyyy", "d-MMM-yyyy", "d MMM yyyy"
        ];

        /// <summary>
        /// A date as the office writes one. Month first (9/23/2026), the Philippine habit; a first
        /// number over 12 can only be a day, so 23/9/2026 still reads. Null when it is not a date.
        /// </summary>
        public static DateOnly? ParseDate(string? text)
        {
            var value = text?.Trim();
            if (string.IsNullOrEmpty(value)) return null;
            if (DateOnly.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var exact))
                return exact;
            var parts = value.Split('/', '-');
            if (parts.Length == 3 && int.TryParse(parts[0], out var first) && first > 12
                && DateOnly.TryParseExact(value.Replace('-', '/'), ["d/M/yyyy", "d/M/yy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var dayFirst))
                return dayFirst;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var loose))
                return DateOnly.FromDateTime(loose);
            return null;
        }

        /// <summary>How far along a paper is: nothing, a stand-in (photocopy / certificate of grades), the original.</summary>
        public static int Rank(DocumentStatus status) => status switch
        {
            DocumentStatus.Submitted => 2,
            DocumentStatus.XeroxCopy or DocumentStatus.CertificateOfGrades => 1,
            _ => 0
        };

        /// <summary>The export's wording, which <see cref="Read"/> reads back.</summary>
        public static string ExportLabel(DocumentStatus status) => status switch
        {
            DocumentStatus.Submitted => "Submitted",
            DocumentStatus.XeroxCopy => "Xerox Copy",
            DocumentStatus.CertificateOfGrades => "Certificate of Grades",
            _ => "Not Submitted"
        };
    }
}
