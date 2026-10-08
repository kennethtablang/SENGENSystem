using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.AcademicRecords
{
    // The backfill path for FR-ENL-01/06 academic history. A school adopting SEN-GEN mid-programme
    // arrives with terms of history already behind it, and the prerequisite gate is worth nothing
    // until that history is in — so the honest first-run story is "import what you have", not "type
    // four years of it one sheet at a time".
    //
    // Follows the pre-enrollment importer's contract deliberately: rows are validated and reported
    // individually, and a bad row never aborts the good ones (FR-PRE-03). The one difference is that
    // a repeated row is an *update* rather than a skip — re-importing a corrected sheet is the
    // normal way a Registrar fixes a mistake, and refusing it would send them to the UI to undo the
    // import by hand.
    public record AcademicRecordImportRow(
        int Row, string StudentNumber, string SubjectCode, string Outcome, IReadOnlyList<string> Errors);

    public record AcademicRecordImportReport(
        int TotalRows, int Loaded, int Updated, int Failed, IReadOnlyList<AcademicRecordImportRow> Rows);

    internal static class XlsxAcademicRecordImporter
    {
        public const long MaxFileBytes = 5 * 1024 * 1024;

        public const string OutcomeLoaded = "Added";
        public const string OutcomeUpdated = "Updated";
        public const string OutcomeFailed = "Failed";

        public static readonly string[] Columns =
            ["StudentNumber", "SubjectCode", "Term", "Verdict", "Remarks"];

        public static async Task<AcademicRecordImportReport> ImportAsync(
            Stream xlsx, AppDbContext db, Guid? recordedByUserId, CancellationToken ct)
        {
            using var workbook = new XLWorkbook(xlsx);
            var sheet = workbook.Worksheets.First();

            var headerMap = MapHeaders(sheet);
            if (!headerMap.ContainsKey("studentnumber")
                || !headerMap.ContainsKey("subjectcode")
                || !headerMap.ContainsKey("verdict"))
            {
                throw new FormatException(
                    "The workbook's first row must be a header row containing at least StudentNumber, "
                    + "SubjectCode, and Verdict. Download the template for the expected layout.");
            }

            // Lookups loaded once. Student numbers match either identifier, because staff will have
            // whichever one their source sheet used — SEN-GEN's own registration number, or the
            // official number issued by the student-records system.
            var registrations = await db.StudentRegistrations.AsNoTracking()
                .Select(r => new { r.Id, r.StudentNumber, r.OfficialStudentNumber })
                .ToListAsync(ct);
            var byStudentNumber = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in registrations)
            {
                byStudentNumber[r.StudentNumber] = r.Id;
                if (!string.IsNullOrWhiteSpace(r.OfficialStudentNumber))
                {
                    byStudentNumber[r.OfficialStudentNumber] = r.Id;
                }
            }

            // A subject code is unique per curriculum, not globally, so the same code can exist in
            // two catalogs. Ambiguity is reported rather than guessed at — silently picking one
            // would write a record against the wrong programme's subject.
            var subjects = await db.Subjects.AsNoTracking()
                .Where(s => !s.IsArchived)
                .Select(s => new { s.Id, s.Code })
                .ToListAsync(ct);
            var subjectsByCode = subjects
                .GroupBy(s => s.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(s => s.Id).ToList(), StringComparer.OrdinalIgnoreCase);

            var semesters = await db.Semesters.AsNoTracking()
                .Select(s => new { s.Id, s.Name, s.IsActive })
                .ToListAsync(ct);
            var semestersByName = semesters.ToDictionary(s => s.Name, s => s.Id, StringComparer.OrdinalIgnoreCase);
            var activeSemesterId = semesters.FirstOrDefault(s => s.IsActive)?.Id;

            // Everything already on file, so a re-import updates rather than colliding with the
            // unique (student, subject, term) index.
            var existing = await db.StudentSubjectRecords
                .ToDictionaryAsync(r => (r.StudentRegistrationId, r.SubjectId, r.SemesterId), ct);

            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
            var results = new List<AcademicRecordImportRow>();
            var loaded = 0;
            var updated = 0;

            // Rows written in this pass, so a file that lists the same subject twice reports the
            // duplicate instead of throwing on save.
            var writtenThisFile = new HashSet<(Guid, Guid, Guid)>();

            for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
            {
                var row = sheet.Row(rowNumber);
                if (row.IsEmpty()) continue;

                string Cell(string column) =>
                    headerMap.TryGetValue(column, out var col) ? row.Cell(col).GetString().Trim() : string.Empty;

                var errors = new List<string>();
                var studentNumber = Cell("studentnumber");
                var subjectCode = Cell("subjectcode");
                var termName = Cell("term");
                var verdictText = Cell("verdict");

                if (studentNumber.Length == 0) errors.Add("StudentNumber is required.");
                if (subjectCode.Length == 0) errors.Add("SubjectCode is required.");

                Guid registrationId = default;
                if (studentNumber.Length > 0 && !byStudentNumber.TryGetValue(studentNumber, out registrationId))
                {
                    errors.Add($"No student found with number {studentNumber}.");
                }

                Guid subjectId = default;
                if (subjectCode.Length > 0)
                {
                    if (!subjectsByCode.TryGetValue(subjectCode, out var matches))
                    {
                        errors.Add($"No live subject found with code {subjectCode}.");
                    }
                    else if (matches.Count > 1)
                    {
                        errors.Add($"{subjectCode} exists in more than one curriculum — it cannot be matched by code alone.");
                    }
                    else
                    {
                        subjectId = matches[0];
                    }
                }

                // A blank term means the active one. That is the common case for a school recording
                // the term that just ended, and typing the full semester name on every row is the
                // kind of friction that gets an import abandoned.
                Guid semesterId = default;
                if (termName.Length == 0)
                {
                    if (activeSemesterId is { } active) semesterId = active;
                    else errors.Add("Term is required — there is no active semester to fall back to.");
                }
                else if (!semestersByName.TryGetValue(termName, out semesterId))
                {
                    errors.Add($"No term named \"{termName}\". Use the exact semester name from the Terms sheet.");
                }

                var verdict = SubjectVerdict.Passed;
                if (verdictText.Length == 0)
                {
                    errors.Add("Verdict is required (Passed, Failed, or Dropped).");
                }
                else if (!Enum.TryParse(verdictText, ignoreCase: true, out verdict) || !Enum.IsDefined(verdict))
                {
                    errors.Add($"\"{verdictText}\" is not a valid verdict — use Passed, Failed, or Dropped.");
                }

                if (errors.Count > 0)
                {
                    results.Add(new AcademicRecordImportRow(
                        rowNumber, studentNumber, subjectCode, OutcomeFailed, errors));
                    continue;
                }

                var key = (registrationId, subjectId, semesterId);
                if (!writtenThisFile.Add(key))
                {
                    results.Add(new AcademicRecordImportRow(
                        rowNumber, studentNumber, subjectCode, OutcomeFailed,
                        ["This file already has a row for the same student, subject, and term."]));
                    continue;
                }

                var remarks = Cell("remarks");
                if (existing.TryGetValue(key, out var record))
                {
                    record.Verdict = verdict;
                    record.Remarks = remarks.Length == 0 ? null : remarks;
                    record.UpdatedAtUtc = DateTime.UtcNow;
                    updated++;
                    results.Add(new AcademicRecordImportRow(
                        rowNumber, studentNumber, subjectCode, OutcomeUpdated, []));
                }
                else
                {
                    db.StudentSubjectRecords.Add(new StudentSubjectRecord
                    {
                        StudentRegistrationId = registrationId,
                        SubjectId = subjectId,
                        SemesterId = semesterId,
                        Verdict = verdict,
                        Remarks = remarks.Length == 0 ? null : remarks,
                        RecordedByUserId = recordedByUserId
                    });
                    loaded++;
                    results.Add(new AcademicRecordImportRow(
                        rowNumber, studentNumber, subjectCode, OutcomeLoaded, []));
                }
            }

            return new AcademicRecordImportReport(
                results.Count, loaded, updated, results.Count(r => r.Outcome == OutcomeFailed), results);
        }

        /// <summary>
        /// The template, with a second sheet listing the exact term names the Term column accepts.
        /// Without it the only way to learn a valid value is to fail an import and read the error.
        /// </summary>
        public static byte[] BuildTemplate(IReadOnlyList<string>? termNames = null)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.AddWorksheet("Academic records");
            for (var i = 0; i < Columns.Length; i++)
            {
                sheet.Cell(1, i + 1).Value = Columns[i];
                sheet.Cell(1, i + 1).Style.Font.Bold = true;
            }

            string[] example = ["2026-000001", "CS101", "", "Passed", "Backfilled from the 2025 class list"];
            for (var i = 0; i < example.Length; i++)
            {
                sheet.Cell(2, i + 1).Value = example[i];
            }
            sheet.Columns().AdjustToContents();

            var notes = workbook.AddWorksheet("How to fill this in");
            string[][] lines =
            [
                ["Column", "What to put"],
                ["StudentNumber", "The SEN-GEN registration number (2026-000001) or the official student number."],
                ["SubjectCode", "The curriculum subject code, e.g. CS101. Must be unique across curricula."],
                ["Term", "The exact semester name. Leave blank to use the active term."],
                ["Verdict", "Passed, Failed, or Dropped. Only Passed earns credit and satisfies a prerequisite."],
                ["Remarks", "Optional note — where the record came from, or why a verdict was corrected."],
                ["", ""],
                ["Re-importing", "A row for a student + subject + term already on file updates it rather than failing."]
            ];
            for (var r = 0; r < lines.Length; r++)
            {
                for (var c = 0; c < lines[r].Length; c++)
                {
                    notes.Cell(r + 1, c + 1).Value = lines[r][c];
                    if (r == 0) notes.Cell(r + 1, c + 1).Style.Font.Bold = true;
                }
            }
            notes.Columns().AdjustToContents();

            // The exact strings the Term column accepts, taken from the terms that actually exist.
            // Without this the only way to learn a valid value is to fail an import and read the error.
            var terms = workbook.AddWorksheet("Terms");
            terms.Cell(1, 1).Value = "Term";
            terms.Cell(1, 1).Style.Font.Bold = true;
            var names = termNames ?? [];
            for (var i = 0; i < names.Count; i++)
            {
                terms.Cell(i + 2, 1).Value = names[i];
            }
            if (names.Count == 0)
            {
                terms.Cell(2, 1).Value = "(no terms have been set up yet)";
            }
            terms.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static Dictionary<string, int> MapHeaders(IXLWorksheet sheet)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var header = sheet.Row(1);
            var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (var col = 1; col <= lastColumn; col++)
            {
                var name = header.Cell(col).GetString().Trim().Replace(" ", "").ToLowerInvariant();
                if (name.Length > 0)
                {
                    map[name] = col;
                }
            }
            return map;
        }
    }
}
