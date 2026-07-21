using ClosedXML.Excel;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Reports.Shared;

namespace SENGENSystem.Server.Features.Reports.FacultyLoading
{
    /// <summary>
    /// The Faculty Teaching Schedule Grid (FR-RPT-02, FR-SCHED-06): one member's week as a
    /// timetable — time slots down, Monday–Saturday across — plus a Daily Class Breakdown
    /// sheet listing the same meetings as rows. The grid is for orientation ("where am I at
    /// 10am Tuesday?"); the breakdown is for reading detail and for filtering in Excel.
    /// </summary>
    public static class FacultyScheduleGridWorkbook
    {
        // The full board day. Wider than the 08:00–17:00 utilization window on purpose: this
        // sheet is the member's personal reference, so a class outside the window must still
        // appear or the timetable would be lying to them about their own week.
        private const int GridStartMinutes = 7 * 60;
        private const int GridEndMinutes = 18 * 60;
        private const int StepMinutes = 30;

        private const int HeaderRow = 7;
        private const int FirstDataRow = HeaderRow + 1;

        private static readonly DayOfWeek[] Days =
        [
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday
        ];

        private static readonly XLColor HeaderFill = XLColor.FromHtml("#003399");
        private static readonly XLColor TimeFill = XLColor.FromHtml("#EEF3FC");
        private static readonly XLColor Grid = XLColor.FromHtml("#C9D5EE");
        private static readonly XLColor ConflictFill = XLColor.FromHtml("#F8B4C4");
        private static readonly XLColor ConflictInk = XLColor.FromHtml("#8C1D3A");
        private static readonly XLColor Muted = XLColor.FromHtml("#5B6C99");

        /// <summary>
        /// Renders the workbook. <paramref name="generatedAt"/> is passed in rather than read
        /// from the clock so the output is deterministic and testable.
        /// </summary>
        public static byte[] Build(
            FacultyProfile faculty, Semester semester,
            List<ScheduleAssignment> meetings, DateTime generatedAt)
        {
            using var workbook = new XLWorkbook();
            GridSheet(workbook, faculty, semester, meetings, generatedAt);
            BreakdownSheet(workbook, faculty, semester, meetings);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        // ---- Sheet 1: the timetable ----------------------------------------------------------

        private static void GridSheet(
            XLWorkbook workbook, FacultyProfile faculty, Semester semester,
            List<ScheduleAssignment> meetings, DateTime generatedAt)
        {
            var sheet = workbook.AddWorksheet("Schedule grid");

            sheet.Cell(1, 1).Value = "Faculty Teaching Schedule";
            sheet.Cell(1, 1).Style.Font.Bold = true;
            sheet.Cell(1, 1).Style.Font.FontSize = 15;
            sheet.Cell(1, 1).Style.Font.FontColor = HeaderFill;

            sheet.Cell(2, 1).Value = faculty.User?.FullName ?? "(unknown)";
            sheet.Cell(2, 1).Style.Font.Bold = true;
            sheet.Cell(2, 1).Style.Font.FontSize = 12;

            Meta(sheet, 3, "Employee ID", string.IsNullOrWhiteSpace(faculty.EmployeeId) ? "—" : faculty.EmployeeId);
            Meta(sheet, 4, "Program", string.IsNullOrWhiteSpace(faculty.ProgramCode) ? "—" : faculty.ProgramCode);
            Meta(sheet, 5, "Semester",
                $"{semester.Name} ({ReportsEndpoints.Humanize(semester.Term.ToString())}, "
                + $"{semester.StartDate:dd MMM yyyy} – {semester.EndDate:dd MMM yyyy})");
            Meta(sheet, 6, "Generated", $"{generatedAt:dd MMM yyyy HH:mm}");

            // ---- Header row: TIME + weekdays ----
            sheet.Cell(HeaderRow, 1).Value = "TIME";
            StyleHeader(sheet.Cell(HeaderRow, 1));
            for (var i = 0; i < Days.Length; i++)
            {
                sheet.Cell(HeaderRow, i + 2).Value = Days[i].ToString();
                StyleHeader(sheet.Cell(HeaderRow, i + 2));
            }

            // ---- Time rows ----
            var rowCount = (GridEndMinutes - GridStartMinutes) / StepMinutes;
            for (var i = 0; i < rowCount; i++)
            {
                var minutes = GridStartMinutes + i * StepMinutes;
                var cell = sheet.Cell(FirstDataRow + i, 1);
                cell.Value = $"{ScheduleGridKit.Hhmm(minutes)}–{ScheduleGridKit.Hhmm(minutes + StepMinutes)}";
                cell.Style.Fill.BackgroundColor = TimeFill;
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontSize = 8;
                cell.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            }

            var lastRow = HeaderRow + rowCount;
            var body = sheet.Range(HeaderRow, 1, lastRow, Days.Length + 1);
            body.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
            body.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            body.Style.Border.SetInsideBorderColor(Grid);
            body.Style.Border.SetOutsideBorderColor(Grid);

            PlaceBlocks(sheet, meetings);

            if (meetings.Count == 0)
            {
                sheet.Cell(lastRow + 2, 1).Value =
                    "No classes are scheduled for this member in this semester.";
                sheet.Cell(lastRow + 2, 1).Style.Font.Italic = true;
                sheet.Cell(lastRow + 2, 1).Style.Font.FontColor = Muted;
            }

            sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            sheet.PageSetup.FitToPages(1, 1);      // a personal timetable should be one page
            sheet.PageSetup.SetRowsToRepeatAtTop(HeaderRow, HeaderRow);
            sheet.PageSetup.Margins.SetTop(0.4).SetBottom(0.4).SetLeft(0.3).SetRight(0.3);
            sheet.SheetView.Freeze(HeaderRow, 1);

            sheet.Column(1).Width = 13;
            for (var i = 0; i < Days.Length; i++) sheet.Column(i + 2).Width = 24;
            sheet.Rows(FirstDataRow, lastRow).Height = 18;
        }

        /// <summary>Merges each meeting into a coloured block, keeping overlaps visible.</summary>
        private static void PlaceBlocks(IXLWorksheet sheet, List<ScheduleAssignment> meetings)
        {
            for (var d = 0; d < Days.Length; d++)
            {
                var column = d + 2;
                var spans = meetings
                    .Where(m => m.TimeSlot!.Day == Days[d])
                    .Select(m => (Meeting: m, Span: ScheduleGridKit.RowSpan(
                        m.TimeSlot!, GridStartMinutes, GridEndMinutes, StepMinutes, FirstDataRow)))
                    .Where(x => x.Span is not null)
                    .Select(x => (x.Meeting, x.Span!.Value.Start, x.Span.Value.End))
                    .ToList();
                if (spans.Count == 0) continue;

                var (clear, overlapping) = ScheduleGridKit.SplitOverlaps(spans, s => (s.Start, s.End));

                foreach (var s in clear)
                {
                    var range = sheet.Range(s.Start, column, s.End, column);
                    range.Merge();
                    range.Value = Block(s.Meeting);
                    range.Style.Fill.BackgroundColor = XLColor.FromHtml(
                        ScheduleGridKit.BlockTints[ScheduleGridKit.TintIndex(s.Meeting.Section?.Subject?.Code)]);
                    range.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                    range.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
                    range.Style.Alignment.SetWrapText(true);
                    range.Style.Font.FontSize = 8;
                    range.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                    range.Style.Border.SetOutsideBorderColor(Grid);
                }

                // A member scheduled in two places at once is a genuine defect in the plan —
                // shown in red rather than silently dropped or crashing the export.
                foreach (var s in overlapping)
                {
                    for (var r = s.Start; r <= s.End; r++)
                    {
                        var cell = sheet.Cell(r, column);
                        var existing = cell.GetString();
                        var label = $"{s.Meeting.Section?.Subject?.Code ?? "?"} "
                            + $"({s.Meeting.Room?.Name ?? "no room"})";
                        cell.Value = string.IsNullOrEmpty(existing)
                            ? $"⚠ DOUBLE-BOOKED: {label}"
                            : $"{existing}  ||  {label}";
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

        /// <summary>Time range, subject code, course title, section, and room — in three lines.</summary>
        private static string Block(ScheduleAssignment m) =>
            $"{ScheduleGridKit.TimeRange(m.TimeSlot!)} · {m.Section?.Subject?.Code ?? "—"}\n"
            + $"{m.Section?.Subject?.Title ?? ""}\n"
            + $"{m.Section?.SectionCode ?? ""} · {m.Room?.Name ?? "No room"}";

        // ---- Sheet 2: Daily Class Breakdown ---------------------------------------------------

        private static void BreakdownSheet(
            XLWorkbook workbook, FacultyProfile faculty, Semester semester, List<ScheduleAssignment> meetings)
        {
            var sheet = workbook.AddWorksheet("Daily Class Breakdown");

            sheet.Cell(1, 1).Value = $"Daily Class Breakdown — {faculty.User?.FullName ?? "(unknown)"}";
            sheet.Cell(1, 1).Style.Font.Bold = true;
            sheet.Cell(1, 1).Style.Font.FontSize = 13;
            sheet.Cell(2, 1).Value = semester.Name;
            sheet.Cell(2, 1).Style.Font.Italic = true;

            const int headerRow = 4;
            string[] headers =
            [
                "Day", "Start", "End", "Hours", "Subject code", "Course title",
                "Units", "Section", "Room", "Building"
            ];
            for (var c = 0; c < headers.Length; c++)
            {
                sheet.Cell(headerRow, c + 1).Value = headers[c];
                StyleHeader(sheet.Cell(headerRow, c + 1));
            }

            var row = headerRow + 1;
            // Grouped by day so the sheet reads as a week, with a per-day total line: the
            // question a member actually asks is "how heavy is my Tuesday?".
            foreach (var day in Days)
            {
                var ofDay = meetings
                    .Where(m => m.TimeSlot!.Day == day)
                    .OrderBy(m => m.TimeSlot!.StartMinutes)
                    .ToList();
                if (ofDay.Count == 0) continue;

                foreach (var m in ofDay)
                {
                    var slot = m.TimeSlot!;
                    sheet.Cell(row, 1).Value = day.ToString();
                    sheet.Cell(row, 2).Value = ScheduleGridKit.Hhmm(slot.StartMinutes);
                    sheet.Cell(row, 3).Value = ScheduleGridKit.Hhmm(slot.EndMinutes);
                    sheet.Cell(row, 4).Value = Math.Round((slot.EndMinutes - slot.StartMinutes) / 60.0, 2);
                    sheet.Cell(row, 5).Value = m.Section?.Subject?.Code ?? "—";
                    sheet.Cell(row, 5).Style.Font.Bold = true;
                    sheet.Cell(row, 6).Value = m.Section?.Subject?.Title ?? "";
                    sheet.Cell(row, 7).Value = m.Section?.Subject?.Units ?? 0;
                    sheet.Cell(row, 8).Value = m.Section?.SectionCode ?? "";
                    sheet.Cell(row, 9).Value = m.Room?.Name ?? "No room";
                    sheet.Cell(row, 10).Value = m.Room?.Building?.Name ?? "";
                    sheet.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml(
                        ScheduleGridKit.BlockTints[ScheduleGridKit.TintIndex(m.Section?.Subject?.Code)]);
                    row++;
                }

                var dayHours = ofDay.Sum(m => m.TimeSlot!.EndMinutes - m.TimeSlot.StartMinutes) / 60.0;
                sheet.Cell(row, 3).Value = $"{day} total";
                sheet.Cell(row, 4).Value = Math.Round(dayHours, 2);
                sheet.Range(row, 1, row, headers.Length).Style.Font.Bold = true;
                sheet.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = TimeFill;
                row++;
            }

            if (meetings.Count == 0)
            {
                sheet.Cell(headerRow + 1, 1).Value = "No classes scheduled this semester.";
                sheet.Cell(headerRow + 1, 1).Style.Font.Italic = true;
                sheet.Cell(headerRow + 1, 1).Style.Font.FontColor = Muted;
            }
            else
            {
                var weekHours = meetings.Sum(m => m.TimeSlot!.EndMinutes - m.TimeSlot.StartMinutes) / 60.0;
                sheet.Cell(row + 1, 3).Value = "Week total";
                sheet.Cell(row + 1, 4).Value = Math.Round(weekHours, 2);
                sheet.Cell(row + 1, 5).Value = $"{meetings.Count} meeting(s)";
                sheet.Range(row + 1, 1, row + 1, headers.Length).Style.Font.Bold = true;
                sheet.Range(row + 1, 1, row + 1, headers.Length).Style.Fill.BackgroundColor =
                    XLColor.FromHtml("#DCE9FB");

                sheet.Range(headerRow, 1, row - 1, headers.Length).SetAutoFilter();
            }

            sheet.SheetView.Freeze(headerRow, 0);
            sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            sheet.PageSetup.FitToPages(1, 0);
            sheet.Columns().AdjustToContents();
        }

        // ---- Shared bits ----------------------------------------------------------------------

        private static void Meta(IXLWorksheet sheet, int row, string label, string value)
        {
            sheet.Cell(row, 1).Value = label;
            sheet.Cell(row, 1).Style.Font.Bold = true;
            sheet.Cell(row, 1).Style.Font.FontColor = Muted;
            sheet.Cell(row, 1).Style.Font.FontSize = 9;
            sheet.Cell(row, 2).Value = value;
            sheet.Cell(row, 2).Style.Font.FontSize = 9;
        }

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
    }
}
