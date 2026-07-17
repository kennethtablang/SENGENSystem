using System.IO.Compression;
using ClosedXML.Excel;
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

        // ---- Consolidated loading report --------------------------------------------------

        private static async Task<IResult> ConsolidatedAsync(
            Guid? semesterId, AppDbContext db, CancellationToken ct)
        {
            var semester = await ReportsEndpoints.ResolveAsync(semesterId, db, ct);
            if (semester is null) return ReportsEndpoints.NoSemester();

            var bytes = await BuildConsolidatedWorkbookAsync(semester, db, ct);
            return Results.File(bytes, ReportsEndpoints.XlsxContentType, "sengen-consolidated-faculty-loading.xlsx");
        }

        internal static async Task<byte[]> BuildConsolidatedWorkbookAsync(
            Semester semester, AppDbContext db, CancellationToken ct)
        {
            var summary = await BuildRowsAsync(semester, db, ct);
            var detail = await db.FacultyLoadAssignments.AsNoTracking()
                .Where(l => l.SemesterId == semester.Id)
                .Include(l => l.FacultyProfile).ThenInclude(f => f!.User)
                .Include(l => l.Subject)
                .Include(l => l.ClassSection)
                .ToListAsync(ct);

            using var workbook = new XLWorkbook();

            var s1 = workbook.AddWorksheet("Summary");
            s1.Cell(1, 1).Value = $"Consolidated Faculty Loading Report — {semester.Name}";
            s1.Cell(1, 1).Style.Font.Bold = true;
            string[] h1 = ["Faculty", "Employee ID", "Program", "Total units", "Max units", "Total subjects", "Scheduled h/week", "Standing"];
            for (var c = 0; c < h1.Length; c++)
            {
                s1.Cell(3, c + 1).Value = h1[c];
                s1.Cell(3, c + 1).Style.Font.Bold = true;
            }
            for (var r = 0; r < summary.Count; r++)
            {
                var row = summary[r];
                s1.Cell(4 + r, 1).Value = row.Name;
                s1.Cell(4 + r, 2).Value = row.EmployeeId;
                s1.Cell(4 + r, 3).Value = row.ProgramCode;
                s1.Cell(4 + r, 4).Value = row.TotalUnits;
                s1.Cell(4 + r, 5).Value = row.MaxUnits;
                s1.Cell(4 + r, 6).Value = row.TotalSubjects;
                s1.Cell(4 + r, 7).Value = row.ScheduledHours;
                s1.Cell(4 + r, 8).Value = row.Standing;
            }
            s1.Columns().AdjustToContents();

            var s2 = workbook.AddWorksheet("Detail");
            string[] h2 = ["Faculty", "Employee ID", "Subject code", "Subject title", "Units", "Hrs/week", "Class section"];
            for (var c = 0; c < h2.Length; c++)
            {
                s2.Cell(1, c + 1).Value = h2[c];
                s2.Cell(1, c + 1).Style.Font.Bold = true;
            }
            var r2 = 2;
            foreach (var l in detail
                .OrderBy(l => l.FacultyProfile?.User?.LastName)
                .ThenBy(l => l.Subject?.Code))
            {
                s2.Cell(r2, 1).Value = l.FacultyProfile?.User?.FullName ?? "(unknown)";
                s2.Cell(r2, 2).Value = l.FacultyProfile?.EmployeeId ?? string.Empty;
                s2.Cell(r2, 3).Value = l.Subject?.Code ?? string.Empty;
                s2.Cell(r2, 4).Value = l.Subject?.Title ?? string.Empty;
                s2.Cell(r2, 5).Value = l.Subject?.Units ?? 0;
                s2.Cell(r2, 6).Value = l.Subject?.Hours ?? 0;
                s2.Cell(r2, 7).Value = l.ClassSection?.DisplayName ?? string.Empty;
                r2++;
            }
            s2.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
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

            var meetings = (await db.ScheduleAssignments.AsNoTracking()
                .Where(a => a.SemesterId == semester.Id)
                .Include(a => a.TimeSlot)
                .Include(a => a.Room)
                .Include(a => a.Section).ThenInclude(s => s!.Subject)
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
                var name = cohort.Key.Length > 31 ? cohort.Key[..31] : cohort.Key;
                var sheet = workbook.AddWorksheet(name);
                sheet.Cell(1, 1).Value = $"Class program — {cohort.Key} · {semester.Name}";
                sheet.Cell(1, 1).Style.Font.Bold = true;

                sheet.Cell(2, 1).Value = "Time";
                var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday };
                for (var d = 0; d < days.Length; d++)
                {
                    sheet.Cell(2, d + 2).Value = days[d].ToString();
                    sheet.Cell(2, d + 2).Style.Font.Bold = true;
                }
                for (var t = gridStart; t < gridEnd; t += step)
                {
                    sheet.Cell(3 + (t - gridStart) / step, 1).Value = $"{Hhmm(t)}–{Hhmm(t + step)}";
                }

                foreach (var m in cohort)
                {
                    var slot = m.TimeSlot!;
                    var col = Array.IndexOf(days, slot.Day) + 2;
                    if (col < 2) continue; // Sunday meetings don't fit the Mon–Sat grid
                    var rowStart = 3 + (Math.Max(slot.StartMinutes, gridStart) - gridStart) / step;
                    var rowEnd = 3 + (Math.Min(slot.EndMinutes, gridEnd) - gridStart) / step - 1;
                    if (rowEnd < rowStart) continue;
                    var range = sheet.Range(rowStart, col, rowEnd, col);
                    range.Merge();
                    range.Value = $"{m.Section!.Subject?.Code} · {m.Room?.Name}";
                    range.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                    range.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
                    range.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#EAF0FB"));
                    range.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                }
                sheet.Columns().AdjustToContents();
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
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
