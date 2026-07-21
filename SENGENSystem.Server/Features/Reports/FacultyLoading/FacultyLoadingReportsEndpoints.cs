using System.IO.Compression;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Reports.FacultyLoading
{
    // Vertical slice: Faculty Academic Load Reports (FR-RPT/FR-FAC). Administrators monitor
    // teaching assignments by semester — searchable list (name or employee ID), per-faculty
    // detailed load workbooks, a consolidated loading report, per-cohort grid schedules, and
    // a bulk .zip bundling everything (incl. room utilization) for institutional compliance.
    public record FacultyLoadingRowDto(
        Guid FacultyProfileId,
        string Name,
        string EmployeeId,
        string ProgramCode,
        int TotalUnits,
        int TotalSubjects,
        int MaxUnits,
        double ScheduledHours,
        string Standing);

    public static class FacultyLoadingReportsEndpoints
    {
        public static IEndpointRouteBuilder MapFacultyLoadingReports(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/reports")
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.Registrar), nameof(UserRole.AcademicHead), nameof(UserRole.SchoolAdmin)));

            group.MapGet("faculty-loading", ListAsync);
            group.MapGet("faculty-loading/consolidated", ConsolidatedAsync);
            group.MapGet("faculty-loading/bulk", BulkAsync);
            group.MapGet("faculty-loading/{facultyProfileId:guid}", IndividualAsync);
            // PDF variants sit alongside the .xlsx downloads rather than replacing them:
            // spreadsheets stay editable for analysis, PDFs are for filing and accreditation.
            group.MapGet("faculty-loading/consolidated.pdf", ConsolidatedPdfAsync);
            group.MapGet("faculty-loading/{facultyProfileId:guid}/pdf", IndividualPdfAsync);
            group.MapGet("faculty-loading/{facultyProfileId:guid}/schedule-grid", ScheduleGridAsync);
            group.MapGet("grid-schedules", GridSchedulesAsync);
            return app;
        }

        // ---- List (powers the page) -------------------------------------------------------

        private static async Task<IResult> ListAsync(
            Guid? semesterId, string? search, AppDbContext db, CancellationToken ct)
        {
            var semester = await ReportsEndpoints.ResolveAsync(semesterId, db, ct);
            if (semester is null) return ReportsEndpoints.NoSemester();

            var rows = await BuildRowsAsync(semester, db, ct);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                rows = rows
                    .Where(r => r.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                        || r.EmployeeId.Contains(term, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return Results.Ok(new
            {
                semesterId = semester.Id,
                semesterName = semester.Name,
                count = rows.Count,
                faculty = rows
            });
        }

        internal static async Task<List<FacultyLoadingRowDto>> BuildRowsAsync(
            Semester semester, AppDbContext db, CancellationToken ct)
        {
            var loads = await db.FacultyLoadAssignments.AsNoTracking()
                .Where(l => l.SemesterId == semester.Id)
                .Join(db.Subjects, l => l.SubjectId, s => s.Id,
                    (l, s) => new { l.FacultyProfileId, s.Units })
                .ToListAsync(ct);
            var byFaculty = loads.GroupBy(x => x.FacultyProfileId)
                .ToDictionary(g => g.Key, g => (Units: g.Sum(x => x.Units), Count: g.Count()));

            var hours = (await db.ScheduleAssignments.AsNoTracking()
                .Where(a => a.SemesterId == semester.Id)
                .Include(a => a.TimeSlot)
                .ToListAsync(ct))
                .Where(a => a.TimeSlot is not null)
                .GroupBy(a => a.FacultyProfileId)
                .ToDictionary(g => g.Key,
                    g => g.Sum(a => a.TimeSlot!.EndMinutes - a.TimeSlot.StartMinutes) / 60.0);

            return (await db.FacultyProfiles.AsNoTracking()
                .Include(f => f.User)
                .OrderBy(f => f.User!.LastName).ThenBy(f => f.User!.FirstName)
                .ToListAsync(ct))
                .Select(f =>
                {
                    byFaculty.TryGetValue(f.Id, out var agg);
                    return new FacultyLoadingRowDto(
                        f.Id,
                        f.User?.FullName ?? "(unknown)",
                        f.EmployeeId,
                        f.ProgramCode,
                        agg.Units,
                        agg.Count,
                        f.MaxLoadUnits,
                        Math.Round(hours.GetValueOrDefault(f.Id), 1),
                        agg.Units > f.MaxLoadUnits ? "Overloaded"
                            : agg.Units == 0 ? "Unassigned"
                            : "Within limit");
                })
                .ToList();
        }

        // ---- Individual detailed load report ---------------------------------------------

        private static async Task<IResult> IndividualAsync(
            Guid facultyProfileId, Guid? semesterId, AppDbContext db, CancellationToken ct)
        {
            var semester = await ReportsEndpoints.ResolveAsync(semesterId, db, ct);
            if (semester is null) return ReportsEndpoints.NoSemester();

            var faculty = await db.FacultyProfiles.AsNoTracking()
                .Include(f => f.User)
                .FirstOrDefaultAsync(f => f.Id == facultyProfileId, ct);
            if (faculty is null) return Results.NotFound(new { message = "Faculty member not found." });

            var bytes = await BuildIndividualWorkbookAsync(faculty, semester, db, ct);
            var slug = (faculty.User?.LastName ?? "faculty").ToLowerInvariant().Replace(' ', '-');
            return Results.File(bytes, ReportsEndpoints.XlsxContentType, $"sengen-load-{slug}.xlsx");
        }

        internal static async Task<byte[]> BuildIndividualWorkbookAsync(
            FacultyProfile faculty, Semester semester, AppDbContext db, CancellationToken ct)
        {
            var loads = await db.FacultyLoadAssignments.AsNoTracking()
                .Where(l => l.SemesterId == semester.Id && l.FacultyProfileId == faculty.Id)
                .Include(l => l.Subject)
                .Include(l => l.ClassSection)
                .ToListAsync(ct);

            var meetings = (await db.ScheduleAssignments.AsNoTracking()
                .Where(a => a.SemesterId == semester.Id && a.FacultyProfileId == faculty.Id)
                .Include(a => a.TimeSlot)
                .Include(a => a.Room)
                .Include(a => a.Section).ThenInclude(s => s!.Subject)
                .ToListAsync(ct))
                .Where(a => a.TimeSlot is not null)
                .OrderBy(a => a.TimeSlot!.Day).ThenBy(a => a.TimeSlot!.StartMinutes)
                .ToList();

            using var workbook = new XLWorkbook();

            var load = workbook.AddWorksheet("Teaching load");
            load.Cell(1, 1).Value = $"Faculty Academic Load Report — {semester.Name}";
            load.Cell(1, 1).Style.Font.Bold = true;
            load.Cell(2, 1).Value = "Faculty";
            load.Cell(2, 2).Value = faculty.User?.FullName ?? "(unknown)";
            load.Cell(3, 1).Value = "Employee ID";
            load.Cell(3, 2).Value = faculty.EmployeeId;
            load.Cell(4, 1).Value = "Program";
            load.Cell(4, 2).Value = faculty.ProgramCode;
            load.Cell(5, 1).Value = "Load ceiling (units)";
            load.Cell(5, 2).Value = faculty.MaxLoadUnits;

            string[] headers = ["Subject code", "Subject title", "Units", "Hrs/week", "Class section", "Type"];
            for (var c = 0; c < headers.Length; c++)
            {
                load.Cell(7, c + 1).Value = headers[c];
                load.Cell(7, c + 1).Style.Font.Bold = true;
            }
            var r0 = 8;
            foreach (var l in loads.OrderBy(l => l.Subject?.Code))
            {
                load.Cell(r0, 1).Value = l.Subject?.Code ?? string.Empty;
                load.Cell(r0, 2).Value = l.Subject?.Title ?? string.Empty;
                load.Cell(r0, 3).Value = l.Subject?.Units ?? 0;
                load.Cell(r0, 4).Value = l.Subject?.Hours ?? 0;
                load.Cell(r0, 5).Value = l.ClassSection?.DisplayName ?? string.Empty;
                load.Cell(r0, 6).Value = l.Subject?.RequiresLaboratory == true ? "Laboratory" : "Lecture";
                r0++;
            }
            load.Cell(r0 + 1, 2).Value = "Total";
            load.Cell(r0 + 1, 2).Style.Font.Bold = true;
            load.Cell(r0 + 1, 3).Value = loads.Sum(l => l.Subject?.Units ?? 0);
            load.Cell(r0 + 1, 4).Value = loads.Sum(l => l.Subject?.Hours ?? 0);
            load.Columns().AdjustToContents();

            var week = workbook.AddWorksheet("Weekly schedule");
            string[] weekHeaders = ["Day", "Start", "End", "Subject", "Section", "Room"];
            for (var c = 0; c < weekHeaders.Length; c++)
            {
                week.Cell(1, c + 1).Value = weekHeaders[c];
                week.Cell(1, c + 1).Style.Font.Bold = true;
            }
            var r1 = 2;
            foreach (var m in meetings)
            {
                week.Cell(r1, 1).Value = m.TimeSlot!.Day.ToString();
                week.Cell(r1, 2).Value = Hhmm(m.TimeSlot.StartMinutes);
                week.Cell(r1, 3).Value = Hhmm(m.TimeSlot.EndMinutes);
                week.Cell(r1, 4).Value = m.Section?.Subject?.Code ?? string.Empty;
                week.Cell(r1, 5).Value = m.Section?.SectionCode ?? string.Empty;
                week.Cell(r1, 6).Value = m.Room?.Name ?? string.Empty;
                r1++;
            }
            week.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        // ---- PDF: Consolidated Faculty Loading Report -------------------------------------

        private const string PdfContentType = "application/pdf";

        /// <summary>Every faculty member's load as one filable PDF, a page-break per member.</summary>
        private static async Task<IResult> ConsolidatedPdfAsync(
            Guid? semesterId, AppDbContext db, CancellationToken ct)
        {
            var semester = await ReportsEndpoints.ResolveAsync(semesterId, db, ct);
            if (semester is null) return ReportsEndpoints.NoSemester();

            var bytes = await BuildPdfAsync(semester, db, null, ct);
            return Results.File(bytes, PdfContentType, "sengen-confirmation-faculty-loading.pdf");
        }

        /// <summary>The same report narrowed to one member.</summary>
        private static async Task<IResult> IndividualPdfAsync(
            Guid facultyProfileId, Guid? semesterId, AppDbContext db, CancellationToken ct)
        {
            var semester = await ReportsEndpoints.ResolveAsync(semesterId, db, ct);
            if (semester is null) return ReportsEndpoints.NoSemester();

            var faculty = await db.FacultyProfiles.AsNoTracking()
                .Include(f => f.User)
                .FirstOrDefaultAsync(f => f.Id == facultyProfileId, ct);
            if (faculty is null) return Results.NotFound(new { message = "Faculty member not found." });

            var bytes = await BuildPdfAsync(semester, db, facultyProfileId, ct);
            var slug = (faculty.User?.LastName ?? "faculty").ToLowerInvariant().Replace(' ', '-');
            return Results.File(bytes, PdfContentType, $"sengen-load-{slug}.pdf");
        }

        internal static async Task<byte[]> BuildPdfAsync(
            Semester semester, AppDbContext db, Guid? onlyFacultyProfileId, CancellationToken ct)
        {
            var reports = await FacultyLoadingPdfData.BuildAsync(semester, db, onlyFacultyProfileId, ct);
            var signatories = await FacultyLoadingSignatories.LoadAsync(db, ct);
            var document = new FacultyLoadingPdfDocument(semester, reports, signatories, DateTime.Now);
            return document.GeneratePdf();
        }

        // ---- Faculty Teaching Schedule Grid ------------------------------------------------

        /// <summary>One member's weekly timetable plus a per-day breakdown (FR-SCHED-06).</summary>
        private static async Task<IResult> ScheduleGridAsync(
            Guid facultyProfileId, Guid? semesterId, AppDbContext db, CancellationToken ct)
        {
            var semester = await ReportsEndpoints.ResolveAsync(semesterId, db, ct);
            if (semester is null) return ReportsEndpoints.NoSemester();

            var faculty = await db.FacultyProfiles.AsNoTracking()
                .Include(f => f.User)
                .FirstOrDefaultAsync(f => f.Id == facultyProfileId, ct);
            if (faculty is null) return Results.NotFound(new { message = "Faculty member not found." });

            var meetings = (await db.ScheduleAssignments.AsNoTracking()
                .Where(a => a.SemesterId == semester.Id && a.FacultyProfileId == facultyProfileId)
                .Include(a => a.TimeSlot)
                .Include(a => a.Room).ThenInclude(r => r!.Building)
                .Include(a => a.Section).ThenInclude(s => s!.Subject)
                .ToListAsync(ct))
                .Where(a => a.TimeSlot is not null)
                .ToList();

            var bytes = FacultyScheduleGridWorkbook.Build(faculty, semester, meetings, DateTime.Now);
            var slug = (faculty.User?.LastName ?? "faculty").ToLowerInvariant().Replace(' ', '-');
            return Results.File(bytes, ReportsEndpoints.XlsxContentType, $"sengen-schedule-grid-{slug}.xlsx");
        }

        // ---- Consolidated loading report --------------------------------------------------

        private static async Task<IResult> ConsolidatedAsync(
            Guid? semesterId, AppDbContext db, CancellationToken ct)
        {
            var semester = await ReportsEndpoints.ResolveAsync(semesterId, db, ct);
            if (semester is null) return ReportsEndpoints.NoSemester();

            var bytes = await BuildConsolidatedWorkbookAsync(semester, db, ct);
            return Results.File(bytes, ReportsEndpoints.XlsxContentType, "sengen-confirmation-faculty-loading.xlsx");
        }

        internal static async Task<byte[]> BuildConsolidatedWorkbookAsync(
            Semester semester, AppDbContext db, CancellationToken ct)
        {
            // Mirrors the PDF: the STI Confirmation of Faculty Loading form, one worksheet per
            // faculty member, so the workbook stays editable while reading as the official form.
            var reports = await FacultyLoadingPdfData.BuildAsync(semester, db, null, ct);
            var signatories = await FacultyLoadingSignatories.LoadAsync(db, ct);

            using var workbook = new XLWorkbook();
            if (reports.Count == 0)
            {
                workbook.AddWorksheet("No faculty").Cell(1, 1).Value =
                    $"No faculty records exist for {semester.Name}.";
            }

            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var report in reports)
            {
                AddConfirmationSheet(workbook, semester, signatories, report, SheetName(report, usedNames));
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        // Excel sheet names: ≤31 chars, unique, no : \ / ? * [ ].
        private static string SheetName(FacultyLoadReportDto r, HashSet<string> used)
        {
            var raw = string.IsNullOrWhiteSpace(r.EmployeeId) || r.EmployeeId == "—" ? r.Name : r.EmployeeId;
            var clean = new string(raw.Select(ch => ":\\/?*[]".Contains(ch) ? '-' : ch).ToArray()).Trim();
            if (clean.Length > 28) clean = clean[..28];
            var name = clean.Length == 0 ? "Faculty" : clean;
            var candidate = name;
            var n = 2;
            while (!used.Add(candidate))
            {
                candidate = $"{name[..Math.Min(name.Length, 26)]} {n++}";
            }
            return candidate;
        }

        private static void AddConfirmationSheet(
            XLWorkbook workbook, Semester semester, FacultyLoadingSignatories sig, FacultyLoadReportDto r, string sheetName)
        {
            var ws = workbook.AddWorksheet(sheetName);
            const int cols = 11; // A..K

            // ---- Title ----
            ws.Range(1, 1, 1, cols).Merge();
            ws.Cell(1, 1).Value = "CONFIRMATION OF FACULTY LOADING";
            ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
            ws.Cell(1, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Range(2, 1, 2, cols).Merge();
            ws.Cell(2, 1).Value = $"{sig.Institution} · {semester.Name}";
            ws.Cell(2, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            // ---- Memo block ----
            void Memo(int row, string label, string value, string? note)
            {
                ws.Cell(row, 1).Value = $"{label} :";
                ws.Cell(row, 1).Style.Font.SetBold();
                ws.Range(row, 2, row, 6).Merge();
                ws.Cell(row, 2).Value = string.IsNullOrWhiteSpace(value) ? "________________________" : value;
                ws.Cell(row, 2).Style.Font.SetBold();
                if (!string.IsNullOrWhiteSpace(note))
                {
                    ws.Range(row, 7, row, cols).Merge();
                    ws.Cell(row, 7).Value = note;
                    ws.Cell(row, 7).Style.Font.SetItalic().Font.SetFontColor(XLColor.FromHtml("#5b6c99"));
                }
            }
            Memo(4, "TO", r.Name, r.EmployeeId == "—" ? null : $"Employee ID {r.EmployeeId}");
            Memo(5, "THRU", sig.ProgramHead, "Program Head");
            Memo(6, "FROM", sig.AcademicHead, "Academic Head");
            Memo(7, "DATE", DateTime.Now.ToString("dd MMMM yyyy"), null);

            ws.Cell(9, 1).Value = "Please be informed that you are assigned the following:";

            // ---- Assignment table ----
            const int headRow = 11;
            string[] headers = ["CODE", "DESCRIPTION", "CLASS NO.", "TYPE", "SECTION", "DAYS", "TIME", "ROOM", "UNITS", "CONTACT HRS", "NO. OF STUDENTS"];
            for (var c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(headRow, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
                cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#003399"));
                cell.Style.Alignment.WrapText = true;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            var row = headRow + 1;
            foreach (var line in r.Lines)
            {
                ws.Cell(row, 1).Value = line.ShowSubjectInfo ? line.SubjectCode : "";
                ws.Cell(row, 2).Value = line.ShowSubjectInfo ? line.SubjectTitle : "";
                ws.Cell(row, 3).Value = line.ShowSubjectInfo ? line.ClassNo : "";
                ws.Cell(row, 4).Value = line.Type;
                ws.Cell(row, 5).Value = line.Section;
                if (line.IsUnscheduled)
                {
                    ws.Range(row, 6, row, 8).Merge();
                    ws.Cell(row, 6).Value = "⚠ Not yet scheduled";
                    ws.Cell(row, 6).Style.Font.SetFontColor(XLColor.FromHtml("#b02a4a")).Font.SetBold();
                }
                else
                {
                    ws.Cell(row, 6).Value = line.Day;
                    ws.Cell(row, 7).Value = line.Time;
                    ws.Cell(row, 8).Value = line.Room;
                }
                if (line.ShowSubjectInfo && line.Units > 0) ws.Cell(row, 9).Value = line.Units;
                if (!line.IsUnscheduled) ws.Cell(row, 10).Value = Math.Round(line.ContactHours, 1);
                if (line.ShowSubjectInfo && line.StudentCount > 0) ws.Cell(row, 11).Value = line.StudentCount;
                row++;
            }

            // ---- Total row ----
            ws.Range(row, 1, row, 8).Merge();
            ws.Cell(row, 1).Value = "TOTAL";
            ws.Cell(row, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            ws.Cell(row, 1).Style.Font.SetBold();
            ws.Cell(row, 9).Value = r.TotalUnits;
            ws.Cell(row, 10).Value = Math.Round(r.TotalContactHours, 1);
            ws.Range(row, 9, row, 11).Style.Font.SetBold();
            ws.Range(row, 1, row, cols).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#dfe7f7"));

            // Table border.
            ws.Range(headRow, 1, row, cols).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            ws.Range(headRow, 1, row, cols).Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);

            // ---- Signatures ----
            var sigRow = row + 3;
            ws.Range(sigRow, 1, sigRow, cols).Merge();
            ws.Cell(sigRow, 1).Value = "Please acknowledge acceptance by affixing your signature on the space provided below.";

            ws.Cell(sigRow + 2, 1).Value = "Conforme:";
            ws.Cell(sigRow + 2, 1).Style.Font.SetBold();
            ws.Cell(sigRow + 2, 7).Value = "Noted:";
            ws.Cell(sigRow + 2, 7).Style.Font.SetBold();

            ws.Cell(sigRow + 5, 1).Value = string.IsNullOrWhiteSpace(r.Name) ? "________________________" : r.Name;
            ws.Cell(sigRow + 5, 1).Style.Font.SetBold().Border.SetTopBorder(XLBorderStyleValues.Thin);
            ws.Cell(sigRow + 6, 1).Value = "Signature over printed name & date";
            ws.Cell(sigRow + 6, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.FromHtml("#5b6c99"));

            ws.Cell(sigRow + 5, 7).Value = string.IsNullOrWhiteSpace(sig.SchoolAdmin) ? "________________________" : sig.SchoolAdmin;
            ws.Cell(sigRow + 5, 7).Style.Font.SetBold().Border.SetTopBorder(XLBorderStyleValues.Thin);
            ws.Cell(sigRow + 6, 7).Value = "School Administrator";
            ws.Cell(sigRow + 6, 7).Style.Font.SetItalic().Font.SetFontColor(XLColor.FromHtml("#5b6c99"));

            ws.Columns().AdjustToContents();
            ws.Column(2).Width = 34;   // Description — keep readable rather than over-wide
            ws.Column(7).Width = 20;   // Time
            ws.Column(11).Width = 12;  // Students header wraps
        }

        // ---- Grid schedules (per cohort timetable) ----------------------------------------

        private static async Task<IResult> GridSchedulesAsync(
            Guid? semesterId, AppDbContext db, CancellationToken ct)
        {
            var semester = await ReportsEndpoints.ResolveAsync(semesterId, db, ct);
            if (semester is null) return ReportsEndpoints.NoSemester();

            var bytes = await BuildGridWorkbookAsync(semester, db, ct);
            return Results.File(bytes, ReportsEndpoints.XlsxContentType, "sengen-grid-schedules.xlsx");
        }

        internal static async Task<byte[]> BuildGridWorkbookAsync(
            Semester semester, AppDbContext db, CancellationToken ct)
        {
            const int gridStart = 7 * 60;   // grid shows the full board day (07:00–18:00)
            const int gridEnd = 18 * 60;
            const int step = 30;
            var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday };

            var meetings = (await db.ScheduleAssignments.AsNoTracking()
                .Where(a => a.SemesterId == semester.Id)
                .Include(a => a.TimeSlot)
                .Include(a => a.Room).ThenInclude(r => r!.Building)
                .Include(a => a.Section).ThenInclude(s => s!.Subject)
                .Include(a => a.FacultyProfile).ThenInclude(f => f!.User)
                .ToListAsync(ct))
                .Where(a => a.TimeSlot is not null && a.Section is not null)
                .ToList();

            using var workbook = new XLWorkbook();
            var cohorts = meetings.GroupBy(a => a.Section!.CohortKey).OrderBy(g => g.Key).ToList();
            if (cohorts.Count == 0)
            {
                workbook.AddWorksheet("No schedule").Cell(1, 1).Value = $"No plotted schedule for {semester.Name}.";
            }

            foreach (var cohort in cohorts)
            {
                var first = cohort.First().Section!;
                var label = $"{first.ProgramCode} {first.YearLevel}-{first.Block}";
                var sheetName = cohort.Key.Length > 31 ? cohort.Key[..31] : cohort.Key;
                var sheet = workbook.AddWorksheet(sheetName);

                // ---- Title band ----
                sheet.Range(1, 1, 1, days.Length + 1).Merge();
                sheet.Cell(1, 1).Value = $"CLASS PROGRAM SCHEDULE — {label}";
                sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(XLColor.White);
                sheet.Cell(1, 1).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#003399"));
                sheet.Cell(1, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                sheet.Range(2, 1, 2, days.Length + 1).Merge();
                sheet.Cell(2, 1).Value = semester.Name;
                sheet.Cell(2, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                sheet.Cell(2, 1).Style.Font.SetFontColor(XLColor.FromHtml("#5b6c99"));

                // ---- Header row (Time + days) ----
                const int headRow = 3;
                sheet.Cell(headRow, 1).Value = "TIME";
                for (var d = 0; d < days.Length; d++)
                {
                    sheet.Cell(headRow, d + 2).Value = days[d].ToString();
                }
                var head = sheet.Range(headRow, 1, headRow, days.Length + 1);
                head.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
                head.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#26437f"));
                head.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                head.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);

                var firstSlotRow = headRow + 1;
                var slotCount = (gridEnd - gridStart) / step;
                for (var i = 0; i < slotCount; i++)
                {
                    var t = gridStart + i * step;
                    var cell = sheet.Cell(firstSlotRow + i, 1);
                    cell.Value = $"{Hhmm(t)}–{Hhmm(t + step)}";
                    cell.Style.Font.SetFontSize(8).Font.SetFontColor(XLColor.FromHtml("#5b6c99"));
                    cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                    sheet.Row(firstSlotRow + i).Height = 15;
                }
                // Empty grid cell borders so the timetable reads as a grid even where free.
                sheet.Range(firstSlotRow, 1, firstSlotRow + slotCount - 1, days.Length + 1)
                    .Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                    .Border.SetInsideBorder(XLBorderStyleValues.Hair).Border.SetInsideBorderColor(XLColor.FromHtml("#dce3f2"));

                // ---- Class blocks, colour-coded per subject (matches the board palette) ----
                foreach (var m in cohort)
                {
                    var slot = m.TimeSlot!;
                    var col = Array.IndexOf(days, slot.Day) + 2;
                    if (col < 2) continue; // Sunday meetings don't fit the Mon–Sat grid
                    var rowStart = firstSlotRow + (Math.Max(slot.StartMinutes, gridStart) - gridStart) / step;
                    var rowEnd = firstSlotRow + (Math.Min(slot.EndMinutes, gridEnd) - gridStart) / step - 1;
                    if (rowEnd < rowStart) continue;

                    var subject = m.Section!.Subject;
                    var hue = HueFor(m.Section.SubjectId);
                    var range = sheet.Range(rowStart, col, rowEnd, col);
                    range.Merge();

                    var cell = sheet.Cell(rowStart, col);
                    var rt = cell.CreateRichText();
                    var code = rt.AddText(subject?.Code ?? "—"); code.Bold = true; code.FontSize = 10;
                    var title = rt.AddText("\n" + (subject?.Title ?? "")); title.FontSize = 7.5;
                    var instr = rt.AddText("\n" + (m.FacultyProfile?.User?.FullName ?? "TBA")); instr.FontSize = 8; instr.Italic = true;
                    var room = rt.AddText($"\n{m.Room?.Name ?? "—"} · {(subject?.RequiresLaboratory == true ? "LAB" : "LEC")}"); room.FontSize = 7.5;
                    var time = rt.AddText($"\n{FacultyLoadingPdfData.H12(slot.StartMinutes)}–{FacultyLoadingPdfData.H12(slot.EndMinutes)}"); time.FontSize = 7; time.FontColor = XLColor.FromHtml("#333333");

                    range.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#" + HslToHex(hue, 0.72, 0.90)));
                    range.Style.Font.SetFontColor(XLColor.FromHtml("#0E2A66"));
                    range.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                    range.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
                    range.Style.Alignment.SetWrapText(true);
                    range.Style.Border.SetOutsideBorder(XLBorderStyleValues.Medium);
                    range.Style.Border.SetOutsideBorderColor(XLColor.FromHtml("#" + HslToHex(hue, 0.55, 0.45)));
                }

                // ---- Legend / subject details below the grid ----
                var legendRow = firstSlotRow + slotCount + 2;
                sheet.Range(legendRow, 1, legendRow, days.Length + 1).Merge();
                sheet.Cell(legendRow, 1).Value = "SUBJECT LEGEND & DETAILS";
                sheet.Cell(legendRow, 1).Style.Font.SetBold().Font.SetFontColor(XLColor.White);
                sheet.Cell(legendRow, 1).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#26437f"));

                var lh = legendRow + 1;
                string[] legendHeaders = ["Colour", "Code", "Subject title", "Instructor", "Type", "Units", "Hrs/wk"];
                for (var c = 0; c < legendHeaders.Length; c++)
                {
                    var cell = sheet.Cell(lh, c + 1);
                    cell.Value = legendHeaders[c];
                    cell.Style.Font.SetBold();
                    cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#dfe7f7"));
                }

                var lr = lh + 1;
                var bySubject = cohort
                    .GroupBy(a => a.Section!.SubjectId)
                    .OrderBy(g => g.First().Section!.Subject?.Code, StringComparer.OrdinalIgnoreCase);
                foreach (var g in bySubject)
                {
                    var s = g.First().Section!.Subject;
                    var hue = HueFor(g.Key);
                    var instructors = string.Join(", ", g
                        .Select(a => a.FacultyProfile?.User?.FullName ?? "TBA")
                        .Distinct());
                    var hrs = g.Sum(a => (a.TimeSlot!.EndMinutes - a.TimeSlot.StartMinutes) / 60.0);

                    sheet.Cell(lr, 1).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#" + HslToHex(hue, 0.72, 0.90)));
                    sheet.Cell(lr, 1).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                        .Border.SetOutsideBorderColor(XLColor.FromHtml("#" + HslToHex(hue, 0.55, 0.45)));
                    sheet.Cell(lr, 2).Value = s?.Code ?? "—";
                    sheet.Cell(lr, 2).Style.Font.SetBold();
                    sheet.Cell(lr, 3).Value = s?.Title ?? "";
                    sheet.Cell(lr, 4).Value = instructors;
                    sheet.Cell(lr, 5).Value = s?.RequiresLaboratory == true ? "LAB" : "LEC";
                    sheet.Cell(lr, 6).Value = s?.Units ?? 0;
                    sheet.Cell(lr, 7).Value = Math.Round(hrs, 1);
                    lr++;
                }
                sheet.Range(lh, 1, lr - 1, legendHeaders.Length).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                sheet.Range(lh, 1, lr - 1, legendHeaders.Length).Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);

                // ---- Sizing & freeze ----
                sheet.Column(1).Width = 13;
                for (var d = 0; d < days.Length; d++) sheet.Column(d + 2).Width = 24;
                sheet.SheetView.FreezeRows(headRow);
                sheet.SheetView.FreezeColumns(1);
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        // Subject colour coding, matching the schedule board's palette so a subject reads the same
        // colour on screen and in the exported grid. Hue is derived from the subject id exactly as
        // the client does (calendarUtils.subjectColor), then rendered as a light fill + darker border.
        private static readonly int[] SubjectHues = { 214, 265, 330, 24, 43, 158, 190, 288, 8, 128, 300, 174 };

        private static int HueFor(Guid subjectId)
        {
            uint h = 0;
            foreach (var ch in subjectId.ToString()) h = h * 31u + ch;
            return SubjectHues[h % (uint)SubjectHues.Length];
        }

        /// <summary>HSL (h in degrees, s/l in 0..1) to an "RRGGBB" hex string.</summary>
        private static string HslToHex(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360;
            var c = (1 - Math.Abs(2 * l - 1)) * s;
            var x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
            var m = l - c / 2;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            int R = (int)Math.Round((r + m) * 255), G = (int)Math.Round((g + m) * 255), B = (int)Math.Round((b + m) * 255);
            return $"{R:X2}{G:X2}{B:X2}";
        }

        // ---- Bulk bundle (.zip) -----------------------------------------------------------

        private static async Task<IResult> BulkAsync(
            Guid? semesterId, AppDbContext db, CancellationToken ct)
        {
            var semester = await ReportsEndpoints.ResolveAsync(semesterId, db, ct);
            if (semester is null) return ReportsEndpoints.NoSemester();

            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                await AddEntryAsync(zip, "consolidated-faculty-loading.xlsx",
                    await BuildConsolidatedWorkbookAsync(semester, db, ct), ct);

                var (headers, rows) = await ReportsEndpoints.RoomUtilizationRowsAsync(semester, db, ct);
                await AddEntryAsync(zip, "room-utilization.xlsx",
                    ReportsEndpoints.BuildWorkbook("Room utilization", semester.Name, headers, rows), ct);

                await AddEntryAsync(zip, "grid-schedules.xlsx",
                    await BuildGridWorkbookAsync(semester, db, ct), ct);

                var faculty = await db.FacultyProfiles.AsNoTracking()
                    .Include(f => f.User)
                    .OrderBy(f => f.User!.LastName)
                    .ToListAsync(ct);
                foreach (var member in faculty)
                {
                    var label = string.IsNullOrWhiteSpace(member.EmployeeId)
                        ? member.User?.LastName?.ToLowerInvariant() ?? member.Id.ToString("N")[..8]
                        : member.EmployeeId;
                    await AddEntryAsync(zip, $"faculty/{Sanitize(label)}.xlsx",
                        await BuildIndividualWorkbookAsync(member, semester, db, ct), ct);
                }
            }

            return Results.File(buffer.ToArray(), "application/zip", "sengen-faculty-load-reports.zip");
        }

        private static async Task AddEntryAsync(ZipArchive zip, string name, byte[] content, CancellationToken ct)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
            await using var stream = entry.Open();
            await stream.WriteAsync(content, ct);
        }

        private static string Sanitize(string s) =>
            string.Concat(s.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-'));

        private static string Hhmm(int minutes) => $"{minutes / 60:D2}:{minutes % 60:D2}";
    }
}
