using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Reports.SemesterExport
{
    // Vertical slice: the one-click "export everything" bundle (FR-RPT-02). A single .xlsx
    // workbook capturing a semester's collected data — registrations, the master schedule,
    // faculty loads, enlistment, room utilization, and document completion — so a finished
    // (typically archived) term can be filed away outside the system.
    public static class SemesterExportEndpoint
    {
        public static IEndpointRouteBuilder MapSemesterExport(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/reports/semester-export", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.Registrar), nameof(UserRole.AcademicHead), nameof(UserRole.SchoolAdmin)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            Guid? semesterId, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var semester = semesterId is { } id
                ? await db.Semesters.AsNoTracking().Include(s => s.SchoolYear).FirstOrDefaultAsync(s => s.Id == id, ct)
                : await db.Semesters.AsNoTracking().Include(s => s.SchoolYear).FirstOrDefaultAsync(s => s.IsActive, ct);
            if (semester is null) return ReportsEndpoints.NoSemester();

            using var workbook = new XLWorkbook();

            var schedule = await MasterScheduleRowsAsync(semester, db, ct);
            var registrations = await ReportsEndpoints.RegistrationRowsAsync(semester, db, ct);
            var loads = await ReportsEndpoints.FacultyLoadRowsAsync(semester, db, ct);
            var enlistment = await ReportsEndpoints.EnlistmentRowsAsync(semester, db, ct);
            var rooms = await ReportsEndpoints.RoomUtilizationRowsAsync(semester, db, ct);
            var documents = await ReportsEndpoints.DocumentCompletionRowsAsync(semester, db, ct);

            WriteOverviewSheet(workbook, semester,
                registrationCount: registrations.Rows.Count,
                sectionCount: enlistment.Rows.Count,
                classCount: schedule.Rows.Count,
                publishedCount: schedule.PublishedCount);

            ReportsEndpoints.WriteTableSheet(workbook, "Registrations", "Validated registrations",
                semester.Name, registrations.Headers, registrations.Rows);
            ReportsEndpoints.WriteTableSheet(workbook, "Master schedule", "Master class schedule",
                semester.Name, schedule.Headers, schedule.Rows);
            ReportsEndpoints.WriteTableSheet(workbook, "Faculty loads", "Faculty load summary",
                semester.Name, loads.Headers, loads.Rows);
            ReportsEndpoints.WriteTableSheet(workbook, "Enlistment", "Enlistment results",
                semester.Name, enlistment.Headers, enlistment.Rows);
            ReportsEndpoints.WriteTableSheet(workbook, "Room utilization", "Room utilization",
                semester.Name, rooms.Headers, rooms.Rows);
            ReportsEndpoints.WriteTableSheet(workbook, "Documents", "Document checklist completion",
                semester.Name, documents.Headers, documents.Rows);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            // A whole-term data dump is worth a line in the audit trail (FR-AUD-01).
            audit.Record(AuditAction.SemesterExported,
                $"Exported the full data bundle for {semester.Name}: " +
                $"{registrations.Rows.Count} registration(s), {schedule.Rows.Count} scheduled class(es), " +
                $"{enlistment.Rows.Count} section(s).",
                "Semester", semester.Id.ToString());
            await db.SaveChangesAsync(ct);

            var slug = semester.Name.ToLowerInvariant()
                .Replace("—", "-").Replace(' ', '-').Replace("--", "-");
            slug = string.Concat(slug.Where(c => char.IsLetterOrDigit(c) || c == '-'));
            return Results.File(stream.ToArray(), ReportsEndpoints.XlsxContentType,
                $"sengen-semester-export-{slug}.xlsx");
        }

        private static void WriteOverviewSheet(
            XLWorkbook workbook, Semester semester,
            int registrationCount, int sectionCount, int classCount, int publishedCount)
        {
            var sheet = workbook.AddWorksheet("Overview");
            sheet.Cell(1, 1).Value = $"SEN-GEN semester data export — {semester.Name}";
            sheet.Cell(1, 1).Style.Font.Bold = true;

            var status = semester.IsActive ? "Active"
                : semester.IsArchived
                    ? $"Archived{(semester.ArchivedAtUtc is { } at ? $" on {at:yyyy-MM-dd} (UTC)" : "")}"
                    : "Inactive";

            (string Label, XLCellValue Value)[] facts =
            [
                ("Semester", semester.Name),
                ("School year", semester.SchoolYear?.Name ?? "—"),
                ("Term dates", $"{semester.StartDate:yyyy-MM-dd} to {semester.EndDate:yyyy-MM-dd}"),
                ("Status", status),
                ("Validated registrations", registrationCount),
                ("Sections offered", sectionCount),
                ("Scheduled classes", classCount),
                ("… of which published", publishedCount),
                ("Exported (UTC)", $"{DateTime.UtcNow:yyyy-MM-dd HH:mm}")
            ];
            for (var i = 0; i < facts.Length; i++)
            {
                sheet.Cell(3 + i, 1).Value = facts[i].Label;
                sheet.Cell(3 + i, 1).Style.Font.Bold = true;
                sheet.Cell(3 + i, 2).Value = facts[i].Value;
            }

            sheet.Cell(3 + facts.Length + 1, 1).Value =
                "Sheets: Registrations · Master schedule · Faculty loads · Enlistment · Room utilization · Documents";
            sheet.Columns().AdjustToContents();
        }

        private static async Task<(string[] Headers, List<object[]> Rows, int PublishedCount)> MasterScheduleRowsAsync(
            Semester semester, AppDbContext db, CancellationToken ct)
        {
            var rows = (await db.ScheduleAssignments.AsNoTracking()
                .Where(a => a.SemesterId == semester.Id)
                .Include(a => a.Section).ThenInclude(s => s!.Subject)
                .Include(a => a.Room)
                .Include(a => a.TimeSlot)
                .Include(a => a.FacultyProfile).ThenInclude(f => f!.User)
                .ToListAsync(ct))
                .Where(a => a.TimeSlot is not null)
                .OrderBy(a => a.Section?.CohortKey)
                .ThenBy(a => a.TimeSlot!.Day)
                .ThenBy(a => a.TimeSlot!.StartMinutes)
                .ToList();

            var table = rows
                .Select(a => new object[]
                {
                    a.Section?.CohortKey ?? string.Empty,
                    a.Section?.Subject?.Code ?? string.Empty,
                    a.Section?.Subject?.Title ?? string.Empty,
                    a.Section?.SectionCode ?? string.Empty,
                    a.TimeSlot!.Day.ToString(),
                    Hhmm(a.TimeSlot.StartMinutes),
                    Hhmm(a.TimeSlot.EndMinutes),
                    a.Room?.Name ?? string.Empty,
                    a.FacultyProfile?.User?.FullName ?? "(unassigned)",
                    a.IsPublished ? "Published" : "Draft"
                })
                .ToList();

            return (
                ["Block", "Subject", "Title", "Section", "Day", "Start", "End", "Room", "Faculty", "Status"],
                table,
                rows.Count(a => a.IsPublished));
        }

        private static string Hhmm(int minutes) => $"{minutes / 60:D2}:{minutes % 60:D2}";
    }
}
