using System.Security.Claims;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Documents.Checklist;

namespace SENGENSystem.Server.Features.Documents.Transfer
{
    // Vertical slice: moving the requirement checklist in and out of a spreadsheet (FR-DOC-02).
    //
    // The admissions office keeps its own workbook and will keep keeping it — so the import reads
    // that workbook as it is, and the export writes one the office can work in and hand back. The
    // export carries the registration number and Student ID on every row, so a sheet that went out
    // through SEN-GEN comes back matched exactly; the office's own sheet is matched by Student ID
    // where it has one, then by name, with anything uncertain put in front of the officer first.
    public static class ChecklistTransferEndpoints
    {
        private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public static IEndpointRouteBuilder MapChecklistTransfer(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/documents")
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.AdmissionOfficer), nameof(UserRole.Registrar), nameof(UserRole.SchoolAdmin)));

            group.MapPost("import/preview", PreviewAsync).DisableAntiforgery();
            group.MapPost("import", CommitAsync).DisableAntiforgery();
            group.MapGet("export", ExportAsync);
            return app;
        }

        // POST /api/documents/import/preview — read the file, match every row, say what would change.
        // Writes nothing.
        private static async Task<IResult> PreviewAsync(
            IFormFile? file, [Microsoft.AspNetCore.Mvc.FromForm] string? options, AppDbContext db, CancellationToken ct)
        {
            var (tables, parsedOptions, problem) = await ReadAsync(file, options);
            if (problem is not null) return problem;

            var plan = await ChecklistImportPlanner.PlanAsync(tables!, parsedOptions!, db, ct);
            var rows = plan.Rows.Select(r => r.Dto).ToList();
            return Results.Ok(new
            {
                fileName = file!.FileName,
                scope = plan.Scope,
                truncated = plan.Truncated,
                sheets = plan.Sheets,
                rows,
                summary = new
                {
                    rows = rows.Count,
                    matched = rows.Count(r => r.Status == MatchStatus.Matched),
                    probable = rows.Count(r => r.Status == MatchStatus.Probable),
                    ambiguous = rows.Count(r => r.Status == MatchStatus.Ambiguous),
                    conflict = rows.Count(r => r.Status == MatchStatus.Conflict),
                    unmatched = rows.Count(r => r.Status == MatchStatus.Unmatched),
                    manual = rows.Count(r => r.Status == MatchStatus.Manual),
                    updates = rows.Where(r => r.Student is not null).Sum(r => r.UpdateCount),
                    studentIdsToRecord = rows.Count(r => r.StudentIdToRecord is not null)
                }
            });
        }

        // POST /api/documents/import — apply the rows the officer accepted. Re-plans from the file
        // against the database as it now is; the client only chooses which rows, never what they say.
        private static async Task<IResult> CommitAsync(
            IFormFile? file, [Microsoft.AspNetCore.Mvc.FromForm] string? options,
            ClaimsPrincipal principal, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var (tables, parsedOptions, problem) = await ReadAsync(file, options);
            if (problem is not null) return problem;
            var opts = parsedOptions!;

            var include = (opts.Include ?? []).ToHashSet(StringComparer.Ordinal);
            if (include.Count == 0)
            {
                return Results.BadRequest(new { message = "Choose at least one row to import." });
            }

            var plan = await ChecklistImportPlanner.PlanAsync(tables!, opts, db, ct);
            var accepted = plan.Rows
                .Where(r => include.Contains(r.Dto.Key) && r.RegistrationId is not null)
                .ToList();
            if (accepted.Count == 0)
            {
                return Results.BadRequest(new { message = "None of the chosen rows is linked to a student." });
            }

            var catalog = await DocumentChecklist.LoadCatalogAsync(db, ct);
            var registrationIds = accepted.Select(r => r.RegistrationId!.Value).Distinct().ToList();
            var registrations = await db.StudentRegistrations
                .Include(r => r.Documents)
                .Where(r => registrationIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, ct);

            Guid? officerId = Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedId)
                && parsedId != Guid.Empty ? parsedId : null;
            var now = DateTime.UtcNow;
            var papersUpdated = 0;
            var studentsUpdated = 0;

            foreach (var group in accepted.GroupBy(r => r.RegistrationId!.Value))
            {
                if (!registrations.TryGetValue(group.Key, out var registration)) continue;

                // The furthest each paper got across this student's rows (they may be on two sheets);
                // for the same status on two rows, the earlier date — that is when it first came in.
                var desired = new Dictionary<string, (DocumentStatus Status, DateOnly? On)>(StringComparer.OrdinalIgnoreCase);
                foreach (var (code, status, on) in group.SelectMany(r => r.Readings))
                {
                    if (!desired.TryGetValue(code, out var have)
                        || ChecklistCellReader.Rank(status) > ChecklistCellReader.Rank(have.Status))
                    {
                        desired[code] = (status, on);
                    }
                    else if (status == have.Status && on is { } d && (have.On is null || d < have.On))
                    {
                        desired[code] = (status, d);
                    }
                }

                var changed = new List<string>();
                foreach (var (code, (status, on)) in desired)
                {
                    if (!catalog.AppliesTo(code, registration.StudentType, registration.Program)) continue;
                    if (!catalog.StatusesFor(code).Contains(status)) continue;

                    var document = registration.Documents.FirstOrDefault(d =>
                        string.Equals(d.RequirementCode, code, StringComparison.OrdinalIgnoreCase));
                    var before = document?.Status ?? DocumentStatus.NotSubmitted;
                    if (before == status && (on is null || on == document?.SubmittedOn)) continue;
                    if (!opts.AllowDowngrade && ChecklistCellReader.Rank(status) < ChecklistCellReader.Rank(before)) continue;

                    if (document is null)
                    {
                        // Applies to them but was never seeded (a requirement widened before
                        // backfill existed). Added through the DbSet so EF inserts it.
                        document = new RegistrationDocument { StudentRegistrationId = registration.Id, RequirementCode = code };
                        db.RegistrationDocuments.Add(document);
                        registration.Documents.Add(document);
                    }
                    if (!DocumentChecklist.Record(document, status, on, officerId, now)) continue;
                    var dated = document.SubmittedOn is { } received ? $" ({received:MMM d, yyyy})" : string.Empty;
                    changed.Add(before == status
                        ? $"{catalog.Label(code)} received on{dated}"
                        : $"{catalog.Label(code)} → {ChecklistCellReader.ExportLabel(status)}{dated}");
                }

                if (changed.Count > 0)
                {
                    papersUpdated += changed.Count;
                    studentsUpdated++;
                    audit.Record(AuditAction.DocumentChecklistUpdated,
                        Truncate($"Imported from \"{file!.FileName}\" for {registration.StudentNumber}: {string.Join("; ", changed)}.", 500),
                        "StudentRegistration", registration.Id.ToString());
                }
            }

            // Student IDs learnt from name-matched rows. Skipped wherever the file contradicts
            // itself (one student given two IDs, or one ID given to two students).
            var idsRecorded = 0;
            if (opts.RecordStudentIds)
            {
                var proposals = accepted
                    .Where(r => r.StudentIdToRecord is not null)
                    .Select(r => (Registration: r.RegistrationId!.Value, Id: r.StudentIdToRecord!.Trim()))
                    .Distinct()
                    .ToList();
                var clean = proposals
                    .Where(p => proposals.Count(o => o.Registration == p.Registration) == 1
                        && proposals.Count(o => StudentMatcher.IdKey(o.Id) == StudentMatcher.IdKey(p.Id)) == 1)
                    .ToList();
                var wanted = clean.Select(p => p.Id).ToList();
                var takenNow = (await db.StudentRegistrations.AsNoTracking()
                        .Where(r => r.OfficialStudentNumber != null)
                        .Select(r => r.OfficialStudentNumber!)
                        .ToListAsync(ct))
                    .Select(StudentMatcher.IdKey)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var (registrationId, studentId) in clean)
                {
                    if (!registrations.TryGetValue(registrationId, out var registration)) continue;
                    if (!string.IsNullOrWhiteSpace(registration.OfficialStudentNumber)) continue;
                    if (takenNow.Contains(StudentMatcher.IdKey(studentId))) continue;

                    registration.OfficialStudentNumber = studentId;
                    registration.OfficialStudentNumberSetAtUtc = now;
                    registration.OfficialStudentNumberSetByUserId = officerId;
                    takenNow.Add(StudentMatcher.IdKey(studentId));
                    idsRecorded++;
                    audit.Record(AuditAction.StudentNumberAssigned,
                        $"Recorded student number {studentId} for {registration.FullName} ({registration.StudentNumber}) " +
                        $"from checklist import \"{Truncate(file!.FileName, 120)}\".",
                        "StudentRegistration", registration.Id.ToString());
                }
            }

            audit.Record(AuditAction.DocumentChecklistImported,
                Truncate($"Imported requirement checklist \"{file!.FileName}\" — {accepted.Count} row(s) applied, " +
                    $"{papersUpdated} paper(s) updated for {studentsUpdated} student(s)" +
                    (idsRecorded > 0 ? $", {idsRecorded} Student ID(s) recorded" : string.Empty) +
                    (opts.AllowDowngrade ? ", downgrades allowed" : string.Empty) + ".", 500),
                "RegistrationDocument", string.Empty);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                rowsApplied = accepted.Count,
                studentsUpdated,
                papersUpdated,
                studentIdsRecorded = idsRecorded
            });
        }

        // GET /api/documents/export — the board as a workbook, filtered as the board is.
        private static async Task<IResult> ExportAsync(
            string? completion, string? search, AppDbContext db, CancellationToken ct)
        {
            var query = await DocumentChecklistEndpoints.BoardQueryAsync(db, completion, search, ct);
            var registrations = await query
                .Include(r => r.Semester)
                .Include(r => r.Documents)
                .OrderBy(r => r.Program).ThenBy(r => r.LastName).ThenBy(r => r.FirstName).ThenBy(r => r.Id)
                .AsSplitQuery()
                .ToListAsync(ct);

            var requirements = await db.AdmissionRequirements.AsNoTracking()
                .Where(r => r.IsActive)
                .Include(r => r.Programs)
                .OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
                .ToListAsync(ct);
            var catalog = await DocumentChecklist.LoadCatalogAsync(db, ct);
            var activeTerm = await db.Semesters.AsNoTracking().Where(s => s.IsActive).Select(s => s.Name).FirstOrDefaultAsync(ct);

            var bytes = BuildWorkbook(registrations, requirements, catalog, activeTerm, completion, search);
            var stamp = InstitutionStamp();
            return Results.File(bytes, XlsxContentType, $"sengen-requirement-checklist-{stamp}.xlsx");
        }

        /// <summary>The header of a paper's date column — the import binds it back to the paper by name.</summary>
        internal static string DateHeader(string requirementName) => $"{requirementName} — Date received";

        /// <summary>
        /// One worksheet per course (ITP, HRS, HRA), so each program's students are grouped the way
        /// the office keeps its own sheets — and each sheet carries only the papers that course
        /// asks for, so ITP has no health-paper columns. Beside every paper is the date it was
        /// received, as a real date cell (sortable, filterable). Every sheet reads back through the
        /// import, matched by registration number.
        /// </summary>
        internal static byte[] BuildWorkbook(
            List<StudentRegistration> registrations, List<AdmissionRequirement> requirements,
            RequirementCatalog catalog, string? activeTerm, string? completion, string? search)
        {
            using var workbook = new XLWorkbook();

            var programs = Enum.GetValues<ProgramTrack>()
                .Where(p => registrations.Any(r => r.Program == p))
                .ToList();
            if (programs.Count == 0) programs = Enum.GetValues<ProgramTrack>().ToList(); // an empty export is still a usable template

            foreach (var program in programs)
            {
                var students = registrations.Where(r => r.Program == program).ToList();
                var papers = requirements.Where(r => r.Programs.Any(p => p.Program == program)).ToList();
                WriteCourseSheet(workbook.AddWorksheet(program.ToString()), students, papers, catalog);
            }

            var about = workbook.AddWorksheet("About this export");
            var lines = new List<string[]>
            {
                new[] { "SEN-GEN requirement checklist" },
                new[] { "Exported", $"{InstitutionClock.Now:MMMM d, yyyy h:mm tt} (PHT)" },
                new[] { "Active term", activeTerm ?? "(none)" },
                new[] { "Filter", string.IsNullOrWhiteSpace(search)
                    ? (string.IsNullOrWhiteSpace(completion) ? "All enrollees in the active term" : $"{completion} checklists, active term")
                    : $"Search \"{search}\" (all terms)" },
                new[] { "Students", registrations.Count.ToString() }
            };
            lines.AddRange(programs.Select(p => new[] { $"  {p}", $"{registrations.Count(r => r.Program == p)} on the {p} sheet" }));
            lines.Add([]);
            lines.Add(["Reading the sheets"]);
            lines.Add(["", "Each course has its own sheet, with only the papers that course requires."]);
            lines.Add(["", "Beside every paper is the date the Admission Office received it. Blank means not received, or received before dates were kept."]);
            lines.Add(["", "N/A means the paper is not asked of that student (e.g. a transcript from a new student)."]);
            lines.Add([]);
            lines.Add(["Re-importing this file"]);
            lines.Add(["", "Edit the status or date columns and import the file on the Document requirements page."]);
            lines.Add(["", "Rows are matched by Registration No. (or Student ID) — keep those two columns as they are."]);
            lines.Add(["", "Statuses read: Submitted, Xerox Copy, Certificate of Grades, Not Submitted. N/A and blank cells change nothing."]);
            lines.Add(["", "A changed date is recorded as the new date received. By default a paper is only moved forward; tick “Allow downgrades” on import to move one back."]);
            for (var r = 0; r < lines.Count; r++)
            {
                for (var c = 0; c < lines[r].Length; c++) about.Cell(r + 1, c + 1).Value = lines[r][c];
                if (lines[r].Length == 1) about.Cell(r + 1, 1).Style.Font.Bold = true;
            }
            about.Cell(1, 1).Style.Font.FontSize = 14;
            about.Column(1).Width = 18;
            about.Column(2).Width = 110;

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static void WriteCourseSheet(
            IXLWorksheet sheet, List<StudentRegistration> students, List<AdmissionRequirement> papers, RequirementCatalog catalog)
        {
            string[] identity =
                ["Registration No.", "Student ID", "Last Name", "First Name", "Middle Name", "Program", "Student Type", "Term", "SIS Status"];
            string[] trailing = ["Papers Received", "Last Received", "Checklist", "Clearance"];
            var firstPaper = identity.Length + 1;
            var lastColumn = identity.Length + papers.Count * 2 + trailing.Length;

            for (var c = 0; c < identity.Length; c++) sheet.Cell(1, c + 1).Value = identity[c];
            for (var i = 0; i < papers.Count; i++)
            {
                sheet.Cell(1, firstPaper + i * 2).Value = papers[i].Name;
                sheet.Cell(1, firstPaper + i * 2 + 1).Value = DateHeader(papers[i].Name);
            }
            for (var t = 0; t < trailing.Length; t++) sheet.Cell(1, firstPaper + papers.Count * 2 + t).Value = trailing[t];

            var header = sheet.Range(1, 1, 1, lastColumn);
            header.Style.Font.Bold = true;
            header.Style.Font.FontColor = XLColor.White;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#003399");
            header.Style.Alignment.WrapText = true;
            header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            // Date headers a shade lighter, so each paper reads as a status + date pair.
            for (var i = 0; i < papers.Count; i++)
            {
                var dateHeader = sheet.Cell(1, firstPaper + i * 2 + 1);
                dateHeader.Style.Fill.BackgroundColor = XLColor.FromHtml("#1A4DB3");
                dateHeader.Style.Font.Bold = false;
            }

            var notSubmittedFill = XLColor.FromHtml("#FFF6CC");
            var muted = XLColor.FromHtml("#7386B3");
            var row = 2;
            foreach (var r in students)
            {
                var applicable = DocumentChecklist.Applicable(r, catalog);
                // Text cells for identity: a Student ID must keep its leading zero, and a
                // registration number must not turn into a date.
                sheet.Cell(row, 1).SetValue(r.StudentNumber);
                sheet.Cell(row, 2).SetValue(r.OfficialStudentNumber ?? string.Empty);
                sheet.Cell(row, 3).SetValue(r.LastName);
                sheet.Cell(row, 4).SetValue(r.FirstName);
                sheet.Cell(row, 5).SetValue(r.MiddleName);
                sheet.Cell(row, 6).SetValue(r.Program.ToString());
                sheet.Cell(row, 7).SetValue(r.StudentType == StudentType.Transferee ? "Transferee" : "New Student");
                sheet.Cell(row, 8).SetValue(r.Semester?.Name ?? string.Empty);
                sheet.Cell(row, 9).SetValue(r.Status.ToString());

                for (var i = 0; i < papers.Count; i++)
                {
                    var statusCell = sheet.Cell(row, firstPaper + i * 2);
                    var dateCell = sheet.Cell(row, firstPaper + i * 2 + 1);
                    var code = papers[i].Code;
                    var document = applicable.FirstOrDefault(d => string.Equals(d.RequirementCode, code, StringComparison.OrdinalIgnoreCase));
                    if (document is null || !catalog.AppliesTo(code, r.StudentType, r.Program))
                    {
                        statusCell.SetValue("N/A");
                        statusCell.Style.Font.FontColor = muted;
                        continue;
                    }
                    statusCell.SetValue(ChecklistCellReader.ExportLabel(document.Status));
                    if (document.Status == DocumentStatus.NotSubmitted)
                    {
                        statusCell.Style.Fill.BackgroundColor = notSubmittedFill;
                    }
                    else if (document.SubmittedOn is { } on)
                    {
                        dateCell.SetValue(on.ToDateTime(TimeOnly.MinValue));
                    }
                }

                var tail = firstPaper + papers.Count * 2;
                sheet.Cell(row, tail).SetValue($"{DocumentChecklist.SubmittedCount(applicable)}/{applicable.Count}");
                if (applicable.Where(d => d.Status != DocumentStatus.NotSubmitted).Max(d => d.SubmittedOn) is { } last)
                {
                    sheet.Cell(row, tail + 1).SetValue(last.ToDateTime(TimeOnly.MinValue));
                }
                sheet.Cell(row, tail + 2).SetValue(DocumentChecklist.IsComplete(applicable) ? "Complete" : "Incomplete");
                var missing = DocumentChecklist.MissingAuthorizationRequirements(applicable, catalog);
                sheet.Cell(row, tail + 3).SetValue(missing.Count == 0 ? "Clearable" : $"Waiting on: {string.Join(", ", missing)}");
                row++;
            }

            var lastRow = Math.Max(row - 1, 2);
            // Dates as dates, written the way the office reads them ("Sep 23, 2026" — never 9/10 vs 10/9).
            for (var i = 0; i < papers.Count; i++)
            {
                var column = sheet.Range(2, firstPaper + i * 2 + 1, lastRow, firstPaper + i * 2 + 1);
                column.Style.DateFormat.Format = "mmm d, yyyy";
                column.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            }
            sheet.Range(2, firstPaper + papers.Count * 2 + 1, lastRow, firstPaper + papers.Count * 2 + 1)
                .Style.DateFormat.Format = "mmm d, yyyy";

            // Drop-downs on the status columns, so a sheet edited for re-import uses words the
            // import reads. Warning style: a remark typed over one is allowed, just flagged.
            if (students.Count > 0)
            {
                for (var i = 0; i < papers.Count; i++)
                {
                    var column = firstPaper + i * 2;
                    var allowed = catalog.StatusesFor(papers[i].Code).Select(ChecklistCellReader.ExportLabel).Append("N/A");
                    var validation = sheet.Range(2, column, row - 1, column).CreateDataValidation();
                    validation.List($"\"{string.Join(",", allowed)}\"", true);
                    validation.ErrorStyle = XLErrorStyle.Warning;
                    validation.ErrorTitle = "Not a checklist status";
                    validation.ErrorMessage = "The import reads Submitted, Xerox Copy / Certificate of Grades, Not Submitted, or N/A.";
                }
            }

            sheet.SheetView.FreezeRows(1);
            sheet.SheetView.FreezeColumns(2);
            sheet.Range(1, 1, Math.Max(row - 1, 1), lastColumn).SetAutoFilter();
            sheet.Columns(1, identity.Length).AdjustToContents();
            for (var i = 0; i < papers.Count; i++)
            {
                sheet.Column(firstPaper + i * 2).Width = 15;
                sheet.Column(firstPaper + i * 2 + 1).Width = 14;
            }
            for (var t = 0; t < trailing.Length; t++) sheet.Column(firstPaper + papers.Count * 2 + t).Width = t == trailing.Length - 1 ? 30 : 14;
            sheet.Row(1).Height = 60;
        }

        /// <summary>The uploaded file and the options JSON, validated — or the reason they were refused.</summary>
        private static async Task<(List<SheetTable>? Tables, ImportOptions? Options, IResult? Problem)> ReadAsync(
            IFormFile? file, string? options)
        {
            if (file is null || file.Length == 0)
                return (null, null, Results.BadRequest(new { message = "Attach an .xlsx or .csv file to import." }));
            if (!ChecklistWorkbookReader.IsSupported(file.FileName))
                return (null, null, Results.BadRequest(new { message = "Only .xlsx workbooks and .csv files are supported. Save older .xls files as .xlsx first." }));
            if (file.Length > ChecklistWorkbookReader.MaxFileBytes)
                return (null, null, Results.BadRequest(new { message = "The file is too large (5 MB max)." }));

            ImportOptions parsed;
            try
            {
                parsed = string.IsNullOrWhiteSpace(options)
                    ? new ImportOptions()
                    : JsonSerializer.Deserialize<ImportOptions>(options, Json) ?? new ImportOptions();
            }
            catch (JsonException)
            {
                return (null, null, Results.BadRequest(new { message = "The import options could not be read." }));
            }

            try
            {
                await using var stream = file.OpenReadStream();
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                buffer.Position = 0;
                var tables = ChecklistWorkbookReader.Read(buffer, file.FileName);
                if (!tables.Any(t => t.Headers.Count > 0))
                {
                    return (null, null, Results.BadRequest(new
                    {
                        message = "No sheet in the file has a student name or Student ID column in its first rows. " +
                            "Export the checklist from this page to see a layout that imports."
                    }));
                }
                return (tables, parsed, null);
            }
            catch (Exception)
            {
                return (null, null, Results.BadRequest(new
                {
                    message = ChecklistWorkbookReader.IsCsv(file.FileName)
                        ? "The CSV file could not be read."
                        : "The file could not be read as an Excel workbook. Save it as .xlsx and try again."
                }));
            }
        }

        private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

        private static string InstitutionStamp() => InstitutionClock.Now.ToString("yyyyMMdd-HHmm");
    }
}
