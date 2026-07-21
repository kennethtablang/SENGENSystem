using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Analytics.RoomUtilization
{
    /// <summary>
    /// The Room Utilization Report (FR-RPT-02): the analysis page's data as a workbook —
    /// an Overview sheet plus one sheet per teaching day, so patterns across the week are
    /// visible rather than averaged into a single number. Under-used rooms are filled red
    /// on every sheet, and each sheet ships with Excel autofilters and frozen headers so
    /// administrators can sort and slice it themselves.
    /// </summary>
    public static class RoomUtilizationExcelEndpoint
    {
        // Fills mirror the analysis page's bands so the workbook and the screen agree.
        private static readonly XLColor HeaderFill = XLColor.FromHtml("#003399");
        private static readonly XLColor CriticalFill = XLColor.FromHtml("#F8D7DF");
        private static readonly XLColor LowFill = XLColor.FromHtml("#FDEFC8");
        private static readonly XLColor CriticalInk = XLColor.FromHtml("#B02A4A");
        private static readonly XLColor TotalFill = XLColor.FromHtml("#EEF3FC");

        public static IEndpointRouteBuilder MapRoomUtilizationExcel(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/analytics/room-utilization/export", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.SchoolAdmin), nameof(UserRole.AcademicHead), nameof(UserRole.Registrar)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            Guid? semesterId, AppDbContext db, CancellationToken ct)
        {
            var semester = await Reports.ReportsEndpoints.ResolveAsync(semesterId, db, ct);
            if (semester is null) return Reports.ReportsEndpoints.NoSemester();

            var bytes = await BuildAsync(semester, db, ct);
            return Results.File(bytes, Reports.ReportsEndpoints.XlsxContentType,
                $"sengen-room-utilization-{Slug(semester.Name)}.xlsx");
        }

        /// <summary>Loads the semester's rooms and placed meetings, then renders the workbook.</summary>
        internal static async Task<byte[]> BuildAsync(Semester semester, AppDbContext db, CancellationToken ct)
        {
            var rooms = await db.Rooms.AsNoTracking()
                .Include(r => r.Building)
                .OrderBy(r => r.Name)
                .ToListAsync(ct);

            var meetings = (await db.ScheduleAssignments.AsNoTracking()
                .Where(a => a.SemesterId == semester.Id)
                .Include(a => a.TimeSlot)
                .Include(a => a.Section).ThenInclude(s => s!.Subject)
                .ToListAsync(ct))
                .Where(a => a.TimeSlot is not null)
                .ToList();

            return Build(semester, rooms, meetings);
        }

        /// <summary>
        /// Renders the workbook from already-loaded data. Kept free of the DbContext so the
        /// sheet layout can be exercised directly, without a database.
        /// </summary>
        public static byte[] Build(Semester semester, List<Room> rooms, List<ScheduleAssignment> meetings)
        {
            using var workbook = new XLWorkbook();
            Overview(workbook, semester, rooms, meetings);

            // One sheet per teaching day. Monday–Friday only: the utilization window is
            // Mon–Fri, so a Saturday tab would score against a denominator that does not
            // exist. Saturday teaching is still counted in the Overview's "total hours".
            for (var day = DayOfWeek.Monday; day <= DayOfWeek.Friday; day++)
            {
                DaySheet(workbook, day, rooms, meetings);
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        // ---- Overview -----------------------------------------------------------------------

        private static void Overview(
            XLWorkbook workbook, Semester semester, List<Room> rooms, List<ScheduleAssignment> meetings)
        {
            var sheet = workbook.AddWorksheet("Overview");

            sheet.Cell(1, 1).Value = "Room Utilization Report";
            sheet.Cell(1, 1).Style.Font.Bold = true;
            sheet.Cell(1, 1).Style.Font.FontSize = 14;
            sheet.Cell(2, 1).Value = semester.Name;
            sheet.Cell(3, 1).Value =
                $"Utilization measured against Mon–Fri {Hhmm(RoomUtilizationAnalysisEndpoint.WindowStartMinutes)}"
                + $"–{Hhmm(RoomUtilizationAnalysisEndpoint.WindowEndMinutes)} "
                + $"({RoomUtilizationAnalysisEndpoint.WindowHoursPerDay:0.#} h/day × "
                + $"{RoomUtilizationAnalysisEndpoint.SchedulableDaysPerWeek} days = "
                + $"{RoomUtilizationAnalysisEndpoint.SchedulableHoursPerWeek:0.#} h/week).";
            sheet.Cell(3, 1).Style.Font.Italic = true;
            sheet.Cell(4, 1).Value = $"Generated {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC";
            sheet.Cell(4, 1).Style.Font.Italic = true;

            const int headerRow = 6;
            string[] headers =
            [
                "Room", "Building", "Capacity", "Type", "Classes",
                "Total hours", "Hours in window", "Utilization %", "Status"
            ];
            WriteHeader(sheet, headerRow, headers);

            var row = headerRow + 1;
            foreach (var r in rooms)
            {
                var mine = meetings.Where(m => m.RoomId == r.Id).ToList();
                var totalHours = mine.Sum(m => m.TimeSlot!.EndMinutes - m.TimeSlot.StartMinutes) / 60.0;
                var windowHours = mine
                    .Where(m => RoomUtilizationAnalysisEndpoint.IsSchedulableDay(m.TimeSlot!.Day))
                    .Sum(m => WindowMinutes(m.TimeSlot!)) / 60.0;
                var pct = Math.Round(
                    100.0 * windowHours / RoomUtilizationAnalysisEndpoint.SchedulableHoursPerWeek, 1);
                var (level, status) = RoomUtilizationAnalysisEndpoint.Classify(pct);

                sheet.Cell(row, 1).Value = r.Name;
                sheet.Cell(row, 2).Value = r.Building?.Name ?? "Unassigned";
                sheet.Cell(row, 3).Value = r.Capacity;
                sheet.Cell(row, 4).Value = r.IsLaboratory ? "Laboratory" : "Lecture";
                sheet.Cell(row, 5).Value = mine.Count;
                sheet.Cell(row, 6).Value = Math.Round(totalHours, 1);
                sheet.Cell(row, 7).Value = Math.Round(windowHours, 1);
                sheet.Cell(row, 8).Value = pct / 100.0;
                sheet.Cell(row, 8).Style.NumberFormat.Format = "0.0%";
                sheet.Cell(row, 9).Value = status;

                Tint(sheet.Range(row, 1, row, headers.Length), level);
                row++;
            }

            if (rooms.Count > 0)
            {
                // A totals line so the sheet answers "how are we doing overall" without a pivot.
                var totalRow = row + 1;
                sheet.Cell(totalRow, 1).Value = "All rooms";
                sheet.Cell(totalRow, 5).FormulaA1 = $"SUM(E{headerRow + 1}:E{row - 1})";
                sheet.Cell(totalRow, 6).FormulaA1 = $"SUM(F{headerRow + 1}:F{row - 1})";
                sheet.Cell(totalRow, 7).FormulaA1 = $"SUM(G{headerRow + 1}:G{row - 1})";
                sheet.Cell(totalRow, 8).FormulaA1 = $"AVERAGE(H{headerRow + 1}:H{row - 1})";
                sheet.Cell(totalRow, 8).Style.NumberFormat.Format = "0.0%";
                var totals = sheet.Range(totalRow, 1, totalRow, headers.Length);
                totals.Style.Font.Bold = true;
                totals.Style.Fill.BackgroundColor = TotalFill;

                Finish(sheet, headerRow, row - 1, headers.Length);
            }

            sheet.Columns().AdjustToContents();
        }

        // ---- One day ------------------------------------------------------------------------

        private static void DaySheet(
            XLWorkbook workbook, DayOfWeek day, List<Room> rooms, List<ScheduleAssignment> meetings)
        {
            var sheet = workbook.AddWorksheet(day.ToString());
            var dayMeetings = meetings.Where(m => m.TimeSlot!.Day == day).ToList();

            sheet.Cell(1, 1).Value = $"{day} — room utilization";
            sheet.Cell(1, 1).Style.Font.Bold = true;
            sheet.Cell(1, 1).Style.Font.FontSize = 13;
            sheet.Cell(2, 1).Value =
                $"Against {RoomUtilizationAnalysisEndpoint.WindowHoursPerDay:0.#} schedulable hours "
                + $"({Hhmm(RoomUtilizationAnalysisEndpoint.WindowStartMinutes)}"
                + $"–{Hhmm(RoomUtilizationAnalysisEndpoint.WindowEndMinutes)}) on this day.";
            sheet.Cell(2, 1).Style.Font.Italic = true;

            const int headerRow = 4;
            string[] headers =
            [
                "Room", "Building", "Capacity", "Type", "Classes",
                "Hours", "Utilization %", "Status", "Schedule"
            ];
            WriteHeader(sheet, headerRow, headers);

            var row = headerRow + 1;
            foreach (var r in rooms)
            {
                var mine = dayMeetings
                    .Where(m => m.RoomId == r.Id)
                    .OrderBy(m => m.TimeSlot!.StartMinutes)
                    .ToList();
                var hours = mine.Sum(m => WindowMinutes(m.TimeSlot!)) / 60.0;
                var pct = Math.Round(
                    100.0 * hours / RoomUtilizationAnalysisEndpoint.WindowHoursPerDay, 1);
                var (level, status) = RoomUtilizationAnalysisEndpoint.Classify(pct);

                sheet.Cell(row, 1).Value = r.Name;
                sheet.Cell(row, 2).Value = r.Building?.Name ?? "Unassigned";
                sheet.Cell(row, 3).Value = r.Capacity;
                sheet.Cell(row, 4).Value = r.IsLaboratory ? "Laboratory" : "Lecture";
                sheet.Cell(row, 5).Value = mine.Count;
                sheet.Cell(row, 6).Value = Math.Round(hours, 1);
                sheet.Cell(row, 7).Value = pct / 100.0;
                sheet.Cell(row, 7).Style.NumberFormat.Format = "0.0%";
                sheet.Cell(row, 8).Value = status;
                // What is actually in the room that day — the reason a percentage is what it is.
                sheet.Cell(row, 9).Value = mine.Count == 0
                    ? "(free all day)"
                    : string.Join(" · ", mine.Select(m =>
                        $"{Hhmm(m.TimeSlot!.StartMinutes)}–{Hhmm(m.TimeSlot.EndMinutes)} "
                        + $"{m.Section?.Subject?.Code ?? "?"}"));

                Tint(sheet.Range(row, 1, row, headers.Length), level);
                row++;
            }

            if (rooms.Count > 0) Finish(sheet, headerRow, row - 1, headers.Length);
            sheet.Columns().AdjustToContents();
            // The schedule column holds long strings; cap it so the sheet stays printable.
            sheet.Column(9).Width = Math.Min(sheet.Column(9).Width, 60);
        }

        // ---- Shared formatting ---------------------------------------------------------------

        private static void WriteHeader(IXLWorksheet sheet, int row, string[] headers)
        {
            for (var c = 0; c < headers.Length; c++)
            {
                var cell = sheet.Cell(row, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = HeaderFill;
            }
        }

        /// <summary>Red for critically under-used rooms, amber for low usage — per FR-RPT-02.</summary>
        private static void Tint(IXLRange range, string level)
        {
            switch (level)
            {
                case "Critical":
                    range.Style.Fill.BackgroundColor = CriticalFill;
                    range.Style.Font.FontColor = CriticalInk;
                    range.Style.Font.Bold = true;
                    break;
                case "Low":
                    range.Style.Fill.BackgroundColor = LowFill;
                    break;
            }
        }

        /// <summary>Autofilter + frozen header, so the sheet is sortable and filterable as shipped.</summary>
        private static void Finish(IXLWorksheet sheet, int headerRow, int lastRow, int columns)
        {
            sheet.Range(headerRow, 1, lastRow, columns).SetAutoFilter();
            sheet.SheetView.Freeze(headerRow, 0);
        }

        /// <summary>Minutes of a slot that fall inside the 08:00–17:00 window.</summary>
        private static int WindowMinutes(TimeSlot slot) => Math.Max(0,
            Math.Min(slot.EndMinutes, RoomUtilizationAnalysisEndpoint.WindowEndMinutes)
            - Math.Max(slot.StartMinutes, RoomUtilizationAnalysisEndpoint.WindowStartMinutes));

        private static string Hhmm(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

        private static string Slug(string value) =>
            new(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
    }
}
