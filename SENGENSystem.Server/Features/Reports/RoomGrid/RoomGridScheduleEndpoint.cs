using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Reports.Shared;

namespace SENGENSystem.Server.Features.Reports.RoomGrid
{
    /// <summary>
    /// The Grid Schedule for Room report (FR-RPT-02, FR-SCHED-06): a visual timetable of
    /// time slots against room columns, one sheet per day. Where the cohort grid
    /// (<c>grid-schedules</c>) answers "what does this block study?", this answers
    /// "what is in this room right now?" — the view needed to spot idle space and
    /// double-booked rooms. Formatted to print on one page wide.
    /// </summary>
    public static class RoomGridScheduleEndpoint
    {
        // The grid deliberately spans wider than the 08:00–17:00 utilization window: a class
        // placed outside the window still occupies the room, and hiding it would make the
        // grid lie about occupancy. 07:00–17:30 in half-hour rows.
        private const int GridStartMinutes = 7 * 60;
        private const int GridEndMinutes = 17 * 60 + 30;
        private const int StepMinutes = 30;

        private const int TitleRow = 1;
        private const int SubtitleRow = 2;
        private const int HeaderRow = 4;

        private static readonly XLColor HeaderFill = XLColor.FromHtml("#003399");
        private static readonly XLColor TimeFill = XLColor.FromHtml("#EEF3FC");
        private static readonly XLColor TodayFill = XLColor.FromHtml("#FFD700");
        private static readonly XLColor ConflictFill = XLColor.FromHtml("#F8B4C4");
        private static readonly XLColor ConflictInk = XLColor.FromHtml("#8C1D3A");
        private static readonly XLColor Grid = XLColor.FromHtml("#C9D5EE");

        public static IEndpointRouteBuilder MapRoomGridSchedule(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/reports/room-grid-schedule", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.SchoolAdmin), nameof(UserRole.AcademicHead), nameof(UserRole.Registrar)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            Guid? semesterId, AppDbContext db, CancellationToken ct)
        {
            var semester = await ReportsEndpoints.ResolveAsync(semesterId, db, ct);
            if (semester is null) return ReportsEndpoints.NoSemester();

            var rooms = await db.Rooms.AsNoTracking()
                .Include(r => r.Building)
                .OrderBy(r => r.Building!.Name).ThenBy(r => r.Name)
                .ToListAsync(ct);

            var meetings = (await db.ScheduleAssignments.AsNoTracking()
                .Where(a => a.SemesterId == semester.Id)
                .Include(a => a.TimeSlot)
                .Include(a => a.Section).ThenInclude(s => s!.Subject)
                .Include(a => a.FacultyProfile).ThenInclude(f => f!.User)
                .ToListAsync(ct))
                .Where(a => a.TimeSlot is not null)
                .ToList();

            var bytes = Build(semester, rooms, meetings, DateTime.Now.DayOfWeek);
            return Results.File(bytes, ReportsEndpoints.XlsxContentType, "sengen-room-grid-schedule.xlsx");
        }

        /// <summary>
        /// Renders the workbook from loaded data. <paramref name="today"/> is passed in rather
        /// than read from the clock so the output is deterministic and testable.
        /// </summary>
        public static byte[] Build(
            Semester semester, List<Room> rooms, List<ScheduleAssignment> meetings, DayOfWeek today)
        {
            using var workbook = new XLWorkbook();

            // Monday-first, through Sunday. Weekend sheets are usually empty but are still
            // emitted: an empty Saturday is information, and Saturday teaching does happen.
            DayOfWeek[] days =
            [
                DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
                DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
            ];

            foreach (var day in days)
            {
                DaySheet(workbook, semester, rooms, meetings.Where(m => m.TimeSlot!.Day == day).ToList(),
                    day, day == today);
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static void DaySheet(
            XLWorkbook workbook, Semester semester, List<Room> rooms,
            List<ScheduleAssignment> dayMeetings, DayOfWeek day, bool isToday)
        {
            var sheet = workbook.AddWorksheet(day.ToString());

            sheet.Cell(TitleRow, 1).Value = $"Room Grid Schedule — {day}";
            sheet.Cell(TitleRow, 1).Style.Font.Bold = true;
            sheet.Cell(TitleRow, 1).Style.Font.FontSize = 14;
            sheet.Cell(SubtitleRow, 1).Value =
                $"{semester.Name} · {Hhmm(GridStartMinutes)}–{Hhmm(GridEndMinutes)}"
                + $" · generated {DateTime.Now:dd MMM yyyy HH:mm}";
            sheet.Cell(SubtitleRow, 1).Style.Font.Italic = true;

            if (isToday)
            {
                // "Today" is called out on the tab and in the title, so a printed page is
                // still self-identifying once it leaves the screen.
                sheet.TabColor = TodayFill;
                var banner = sheet.Cell(TitleRow, 1);
                banner.Value = $"Room Grid Schedule — {day}  (TODAY)";
                banner.Style.Fill.BackgroundColor = TodayFill;
            }

            if (rooms.Count == 0)
            {
                sheet.Cell(HeaderRow, 1).Value = "No rooms are configured.";
                return;
            }

            // ---- Header: time column + one column per room ----
            sheet.Cell(HeaderRow, 1).Value = "TIME";
            StyleHeader(sheet.Cell(HeaderRow, 1));
            for (var i = 0; i < rooms.Count; i++)
            {
                var cell = sheet.Cell(HeaderRow, i + 2);
                cell.Value = rooms[i].Building?.Code is { Length: > 0 } code
                    ? $"{rooms[i].Name} ({code})"
                    : rooms[i].Name;
                StyleHeader(cell);
            }

            // ---- Time rows ----
            var rowCount = (GridEndMinutes - GridStartMinutes) / StepMinutes;
            for (var i = 0; i < rowCount; i++)
            {
                var minutes = GridStartMinutes + i * StepMinutes;
                var cell = sheet.Cell(HeaderRow + 1 + i, 1);
                cell.Value = $"{Hhmm(minutes)}–{Hhmm(minutes + StepMinutes)}";
                cell.Style.Fill.BackgroundColor = TimeFill;
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontSize = 8;
                cell.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            }

            var lastRow = HeaderRow + rowCount;
            var body = sheet.Range(HeaderRow, 1, lastRow, rooms.Count + 1);
            body.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
            body.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            body.Style.Border.SetInsideBorderColor(Grid);
            body.Style.Border.SetOutsideBorderColor(Grid);

            // ---- Place the blocks ----
            PlaceBlocks(sheet, rooms, dayMeetings);

            // ---- Print setup ----
            sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            sheet.PageSetup.FitToPages(1, 0);          // one page wide, as many tall as needed
            sheet.PageSetup.SetRowsToRepeatAtTop(HeaderRow, HeaderRow);
            sheet.PageSetup.SetColumnsToRepeatAtLeft(1, 1);
            sheet.PageSetup.Margins.SetTop(0.4).SetBottom(0.4).SetLeft(0.3).SetRight(0.3);
            sheet.PageSetup.Header.Center.AddText($"{semester.Name} — {day}");
            sheet.PageSetup.Footer.Right.AddText("Page ");
            sheet.PageSetup.Footer.Right.AddText(XLHFPredefinedText.PageNumber);
            sheet.SheetView.Freeze(HeaderRow, 1);

            sheet.Column(1).Width = 13;
            for (var i = 0; i < rooms.Count; i++) sheet.Column(i + 2).Width = 20;
            sheet.Rows(HeaderRow + 1, lastRow).Height = 15;
        }

        /// <summary>
        /// Merges each meeting into a coloured block. Meetings that overlap in the same room
        /// cannot be merged — they are rendered cell-by-cell in red instead, which is how a
        /// double-booking becomes visible rather than an exception.
        /// </summary>
        private static void PlaceBlocks(IXLWorksheet sheet, List<Room> rooms, List<ScheduleAssignment> dayMeetings)
        {
            for (var i = 0; i < rooms.Count; i++)
            {
                var column = i + 2;
                var inRoom = dayMeetings
                    .Where(m => m.RoomId == rooms[i].Id)
                    .OrderBy(m => m.TimeSlot!.StartMinutes)
                    .ToList();
                if (inRoom.Count == 0) continue;

                // Row span each meeting claims, clipped to the visible grid.
                var spans = inRoom
                    .Select(m => (Meeting: m, Span: RowSpan(m.TimeSlot!)))
                    .Where(x => x.Span is not null)
                    .Select(x => (x.Meeting, Start: x.Span!.Value.Start, End: x.Span.Value.End))
                    .ToList();

                var (clear, overlapping) = ScheduleGridKit.SplitOverlaps(spans, s => (s.Start, s.End));

                foreach (var s in clear)
                {
                    var range = sheet.Range(s.Start, column, s.End, column);
                    range.Merge();
                    range.Value = Label(s.Meeting, compact: false);
                    range.Style.Fill.BackgroundColor = XLColor.FromHtml(
                        ScheduleGridKit.BlockTints[ScheduleGridKit.TintIndex(s.Meeting.Section?.Subject?.Code)]);
                    range.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                    range.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
                    range.Style.Alignment.SetWrapText(true);
                    range.Style.Font.FontSize = 8;
                    range.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                    range.Style.Border.SetOutsideBorderColor(Grid);
                }

                // Overlaps are written cell-by-cell instead of merged, so a double-booking
                // shows up as a red band naming everything that wants the room.
                foreach (var s in overlapping)
                {
                    for (var r = s.Start; r <= s.End; r++)
                    {
                        var cell = sheet.Cell(r, column);
                        var existing = cell.GetString();
                        var label = Label(s.Meeting, compact: true);
                        cell.Value = string.IsNullOrEmpty(existing) ? $"⚠ {label}" : $"{existing}  ||  {label}";
                        cell.Style.Fill.BackgroundColor = ConflictFill;
                        cell.Style.Font.FontColor = ConflictInk;
                        cell.Style.Font.Bold = true;
                        cell.Style.Font.FontSize = 7;
                        cell.Style.Alignment.SetWrapText(true);
                        cell.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
                    }
                }
            }
        }

        /// <summary>Subject code, faculty name, and section — the three things the grid must say.</summary>
        private static string Label(ScheduleAssignment m, bool compact)
        {
            var code = m.Section?.Subject?.Code ?? "—";
            var faculty = m.FacultyProfile?.User?.FullName ?? "Unassigned";
            var section = m.Section?.SectionCode ?? "";
            return compact
                ? $"{code} / {section}"
                : $"{code}\n{faculty}\n{section}";
        }

        private static (int Start, int End)? RowSpan(TimeSlot slot) => ScheduleGridKit.RowSpan(
            slot, GridStartMinutes, GridEndMinutes, StepMinutes, HeaderRow + 1);

        private static void StyleHeader(IXLCell cell)
        {
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Font.FontSize = 9;
            cell.Style.Fill.BackgroundColor = HeaderFill;
            cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            cell.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            cell.Style.Alignment.SetWrapText(true);
        }

        private static string Hhmm(int minutes) => ScheduleGridKit.Hhmm(minutes);
    }
}
