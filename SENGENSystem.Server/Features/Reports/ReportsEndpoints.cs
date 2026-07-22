using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Documents;

namespace SENGENSystem.Server.Features.Reports
{
    // Vertical slice: semester-scoped operational reports (FR-RPT-01), each viewable as JSON
    // and exportable as .xlsx for institutional planning (FR-RPT-02): validated registrations,
    // enlistment results, faculty load summary, room utilization, and document completion.
    public static class ReportsEndpoints
    {
        // Utilization window per the institutional standard: Mon–Fri, 08:00–17:00 (9 h/day).
        internal const int WindowStartMinutes = 8 * 60;
        internal const int WindowEndMinutes = 17 * 60;
        private const double WindowHoursPerWeek = 5 * (WindowEndMinutes - WindowStartMinutes) / 60.0;

        public static IEndpointRouteBuilder MapReports(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/reports")
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.Registrar), nameof(UserRole.AcademicHead), nameof(UserRole.SchoolAdmin)));

            group.MapGet("registration", RegistrationAsync);
            group.MapGet("enlistment", EnlistmentAsync);
            group.MapGet("faculty-load", FacultyLoadAsync);
            group.MapGet("room-utilization", RoomUtilizationAsync);
            group.MapGet("document-completion", DocumentCompletionAsync);
            return app;
        }

        // ---- Report builders ------------------------------------------------------------

        private static async Task<IResult> RegistrationAsync(
            Guid? semesterId, string? format, AppDbContext db, CancellationToken ct)
        {
            var semester = await ResolveAsync(semesterId, db, ct);
            if (semester is null) return NoSemester();

            var (headers, rows) = await RegistrationRowsAsync(semester, db, ct);
            return Deliver("Validated registrations", semester, format, headers, rows);
        }

        /// <summary>Shared with the full semester export bundle.</summary>
        internal static async Task<(string[] Headers, List<object[]> Rows)> RegistrationRowsAsync(
            Semester semester, AppDbContext db, CancellationToken ct)
        {
            var rows = (await db.StudentRegistrations.AsNoTracking()
                .Where(r => r.SemesterId == semester.Id)
                .Include(r => r.Documents)
                .OrderBy(r => r.StudentNumber)
                .ToListAsync(ct))
                .Select(r => new object[]
                {
                    r.StudentNumber, r.FullName, r.Program.ToString(), Humanize(r.StudentType.ToString()),
                    r.Status.ToString(), r.Email,
                    DocumentChecklist.SubmittedCount(r.Documents), r.Documents.Count,
                    r.IsPreAuthorized ? "Yes" : "No"
                })
                .ToList();

            return (
                ["Student no.", "Name", "Program", "Type", "Status", "Email", "Docs submitted", "Docs required", "Pre-authorized"],
                rows);
        }

        private static async Task<IResult> EnlistmentAsync(
            Guid? semesterId, string? format, AppDbContext db, CancellationToken ct)
        {
            var semester = await ResolveAsync(semesterId, db, ct);
            if (semester is null) return NoSemester();

            var (headers, rows) = await EnlistmentRowsAsync(semester, db, ct);
            return Deliver("Enlistment results", semester, format, headers, rows);
        }

        /// <summary>Shared with the full semester export bundle.</summary>
        internal static async Task<(string[] Headers, List<object[]> Rows)> EnlistmentRowsAsync(
            Semester semester, AppDbContext db, CancellationToken ct)
        {
            var requests = await db.SlotRequests.AsNoTracking()
                .Where(r => r.Section!.SemesterId == semester.Id)
                .ToListAsync(ct);
            var bySection = requests.GroupBy(r => r.SectionId).ToDictionary(g => g.Key, g => g.ToList());

            var rows = (await db.Sections.AsNoTracking()
                .Where(s => s.SemesterId == semester.Id)
                .Include(s => s.Subject)
                .OrderBy(s => s.Subject!.Code).ThenBy(s => s.SectionCode)
                .ToListAsync(ct))
                .Select(s =>
                {
                    var reqs = bySection.GetValueOrDefault(s.Id, []);
                    return new object[]
                    {
                        s.Subject?.Code ?? string.Empty, s.Subject?.Title ?? string.Empty, s.SectionCode, s.CohortKey,
                        s.EnrolledCount, s.Capacity,
                        s.Capacity == 0 ? 0 : Math.Round(100.0 * s.EnrolledCount / s.Capacity, 1),
                        reqs.Count(r => r.Status == SlotRequestStatus.Requested),
                        reqs.Count(r => r.Status == SlotRequestStatus.Rejected)
                    };
                })
                .ToList();

            return (
                ["Subject", "Title", "Section", "Block", "Enrolled", "Capacity", "Fill %", "Pending requests", "Rejected"],
                rows);
        }

        private static async Task<IResult> FacultyLoadAsync(
            Guid? semesterId, string? format, AppDbContext db, CancellationToken ct)
        {
            var semester = await ResolveAsync(semesterId, db, ct);
            if (semester is null) return NoSemester();

            var (headers, rows) = await FacultyLoadRowsAsync(semester, db, ct);
            return Deliver("Faculty load summary", semester, format, headers, rows);
        }

        /// <summary>Shared with the full semester export bundle.</summary>
        internal static async Task<(string[] Headers, List<object[]> Rows)> FacultyLoadRowsAsync(
            Semester semester, AppDbContext db, CancellationToken ct)
        {
            var unitsByFaculty = (await db.FacultyLoadAssignments.AsNoTracking()
                .Where(l => l.SemesterId == semester.Id)
                .Join(db.Subjects, l => l.SubjectId, s => s.Id, (l, s) => new { l.FacultyProfileId, s.Units })
                .ToListAsync(ct))
                .GroupBy(x => x.FacultyProfileId)
                .ToDictionary(g => g.Key, g => (Units: g.Sum(x => x.Units), Count: g.Count()));

            var hours = (await db.ScheduleAssignments.AsNoTracking()
                .Where(a => a.SemesterId == semester.Id)
                .Include(a => a.TimeSlot)
                .ToListAsync(ct))
                .Where(a => a.TimeSlot is not null)
                .GroupBy(a => a.FacultyProfileId)
                .ToDictionary(g => g.Key, g => g.Sum(a => a.TimeSlot!.EndMinutes - a.TimeSlot.StartMinutes) / 60.0);

            var rows = (await db.FacultyProfiles.AsNoTracking()
                .Include(f => f.User)
                .OrderBy(f => f.User!.LastName).ThenBy(f => f.User!.FirstName)
                .ToListAsync(ct))
                .Select(f =>
                {
                    unitsByFaculty.TryGetValue(f.Id, out var agg);
                    return new object[]
                    {
                        f.User?.FullName ?? "(unknown)", f.ProgramCode,
                        agg.Units, f.MaxLoadUnits, agg.Count,
                        Math.Round(hours.GetValueOrDefault(f.Id), 1),
                        agg.Units > f.MaxLoadUnits ? "Overloaded" : "Within limit"
                    };
                })
                .ToList();

            return (
                ["Faculty", "Program", "Assigned units", "Max units", "Subject loads", "Scheduled h/week", "Standing"],
                rows);
        }

        private static async Task<IResult> RoomUtilizationAsync(
            Guid? semesterId, string? format, AppDbContext db, CancellationToken ct)
        {
            var semester = await ResolveAsync(semesterId, db, ct);
            if (semester is null) return NoSemester();

            var (headers, rows) = await RoomUtilizationRowsAsync(semester, db, ct);
            return Deliver("Room utilization", semester, format, headers, rows);
        }

        /// <summary>Shared with the faculty-loading bulk download, which bundles this report.</summary>
        internal static async Task<(string[] Headers, List<object[]> Rows)> RoomUtilizationRowsAsync(
            Semester semester, AppDbContext db, CancellationToken ct)
        {
            // Only the part of a class that falls inside the 08:00–17:00 window counts toward
            // utilization, so early/late meetings cannot inflate the percentage.
            var assignments = (await db.ScheduleAssignments.AsNoTracking()
                .Where(a => a.SemesterId == semester.Id)
                .Include(a => a.TimeSlot)
                .ToListAsync(ct))
                .Where(a => a.TimeSlot is not null)
                .ToList();
            var byRoom = assignments.GroupBy(a => a.RoomId).ToDictionary(
                g => g.Key,
                g => (
                    Classes: g.Count(),
                    Hours: g.Sum(a => a.TimeSlot!.EndMinutes - a.TimeSlot.StartMinutes) / 60.0,
                    WindowHours: g.Sum(a => Math.Max(0,
                        Math.Min(a.TimeSlot!.EndMinutes, WindowEndMinutes)
                        - Math.Max(a.TimeSlot.StartMinutes, WindowStartMinutes))) / 60.0));

            var rows = (await db.Rooms.AsNoTracking()
                .Include(r => r.Building)
                .OrderBy(r => r.Name)
                .ToListAsync(ct))
                .Select(r =>
                {
                    byRoom.TryGetValue(r.Id, out var agg);
                    return new object[]
                    {
                        r.Name, r.Building?.Name ?? string.Empty, r.Capacity,
                        r.Kind.Label(),
                        agg.Classes, Math.Round(agg.Hours, 1),
                        Math.Round(agg.WindowHours, 1),
                        Math.Round(100.0 * agg.WindowHours / WindowHoursPerWeek, 1)
                    };
                })
                .ToList();

            return (
                ["Room", "Building", "Capacity", "Type", "Classes", "Total hours/week", "Hours in 08:00–17:00", "Utilization % (Mon–Fri 08:00–17:00)"],
                rows);
        }

        private static async Task<IResult> DocumentCompletionAsync(
            Guid? semesterId, string? format, AppDbContext db, CancellationToken ct)
        {
            var semester = await ResolveAsync(semesterId, db, ct);
            if (semester is null) return NoSemester();

            var (headers, rows) = await DocumentCompletionRowsAsync(semester, db, ct);
            return Deliver("Document checklist completion", semester, format, headers, rows);
        }

        /// <summary>Shared with the full semester export bundle.</summary>
        internal static async Task<(string[] Headers, List<object[]> Rows)> DocumentCompletionRowsAsync(
            Semester semester, AppDbContext db, CancellationToken ct)
        {
            var rows = (await db.StudentRegistrations.AsNoTracking()
                .Where(r => r.SemesterId == semester.Id)
                .Include(r => r.Documents)
                .OrderBy(r => r.StudentNumber)
                .ToListAsync(ct))
                .Select(r =>
                {
                    var missing = r.Documents
                        .Where(d => d.Status == DocumentStatus.NotSubmitted)
                        .Select(d => DocumentChecklist.Label(d.DocumentType));
                    return new object[]
                    {
                        r.StudentNumber, r.FullName, r.Program.ToString(),
                        DocumentChecklist.SubmittedCount(r.Documents), r.Documents.Count,
                        DocumentChecklist.IsComplete(r.Documents) ? "Complete" : "Incomplete",
                        string.Join("; ", missing)
                    };
                })
                .ToList();

            return (
                ["Student no.", "Name", "Program", "Submitted", "Required", "Checklist", "Missing papers"],
                rows);
        }

        // ---- Shared delivery -------------------------------------------------------------

        internal static async Task<Semester?> ResolveAsync(Guid? semesterId, AppDbContext db, CancellationToken ct) =>
            semesterId is { } id
                ? await db.Semesters.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct)
                : await db.Semesters.AsNoTracking().FirstOrDefaultAsync(s => s.IsActive, ct);

        internal static IResult NoSemester() =>
            Results.BadRequest(new { message = "No target semester found." });

        internal const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        /// <summary>Renders a one-sheet tabular workbook; shared with the faculty-loading bundle.</summary>
        internal static byte[] BuildWorkbook(string title, string semesterName, string[] headers, List<object[]> rows)
        {
            using var workbook = new XLWorkbook();
            WriteTableSheet(workbook, title, title, semesterName, headers, rows);
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        /// <summary>Adds one titled tabular sheet; shared with the full semester export bundle.</summary>
        internal static void WriteTableSheet(
            XLWorkbook workbook, string sheetName, string title, string semesterName,
            string[] headers, List<object[]> rows)
        {
            var sheet = workbook.AddWorksheet(sheetName.Length > 31 ? sheetName[..31] : sheetName);
            sheet.Cell(1, 1).Value = $"{title} — {semesterName}";
            sheet.Cell(1, 1).Style.Font.Bold = true;
            for (var c = 0; c < headers.Length; c++)
            {
                sheet.Cell(3, c + 1).Value = headers[c];
                sheet.Cell(3, c + 1).Style.Font.Bold = true;
            }
            for (var r = 0; r < rows.Count; r++)
            {
                for (var c = 0; c < rows[r].Length; c++)
                {
                    sheet.Cell(4 + r, c + 1).Value = XLCellValue.FromObject(rows[r][c]);
                }
            }
            sheet.Columns().AdjustToContents();
        }

        /// <summary>JSON by default; ?format=xlsx streams a ClosedXML workbook (FR-RPT-02).</summary>
        internal static IResult Deliver(
            string title, Semester semester, string? format, string[] headers, List<object[]> rows)
        {
            if (string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase))
            {
                var slug = title.ToLowerInvariant().Replace(' ', '-');
                return Results.File(
                    BuildWorkbook(title, semester.Name, headers, rows),
                    XlsxContentType,
                    $"sengen-{slug}.xlsx");
            }

            return Results.Ok(new
            {
                title,
                semesterId = semester.Id,
                semesterName = semester.Name,
                headers,
                rows,
                count = rows.Count
            });
        }

        internal static string Humanize(string pascal)
        {
            var spaced = System.Text.RegularExpressions.Regex.Replace(pascal, "([a-z])([A-Z])", "$1 $2");
            return spaced.Length == 0 ? spaced : char.ToUpperInvariant(spaced[0]) + spaced[1..].ToLowerInvariant();
        }
    }
}
