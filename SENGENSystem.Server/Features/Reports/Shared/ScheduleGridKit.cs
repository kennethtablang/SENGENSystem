using SENGENSystem.Server.Common.Formatting;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Reports.Shared
{
    /// <summary>
    /// Shared machinery for the timetable-style Excel reports. The grids differ in what their
    /// columns mean — rooms for the room grid, weekdays for the faculty grid — but they agree
    /// on how a time slot maps to rows and how an overlap is detected. Keeping that here stops
    /// the grids from drifting apart. Block colours come from <c>Common.Reporting.SubjectPalette</c>
    /// — the board's palette — rather than a report-only tint set, which used to give a subject a
    /// different colour in print than on screen.
    /// </summary>
    internal static class ScheduleGridKit
    {
        /// <summary>A slot's "HH:mm–HH:mm" range.</summary>
        internal static string TimeRange(TimeSlot slot) =>
            $"{ClockText.Hhmm(slot.StartMinutes)}–{ClockText.Hhmm(slot.EndMinutes)}";

        /// <summary>
        /// The grid rows a slot covers, or null when it falls entirely outside the visible grid.
        /// The end is rounded up so a class finishing mid-row still fills that row.
        /// </summary>
        internal static (int Start, int End)? RowSpan(
            TimeSlot slot, int gridStartMinutes, int gridEndMinutes, int stepMinutes, int firstDataRow)
        {
            var start = Math.Max(slot.StartMinutes, gridStartMinutes);
            var end = Math.Min(slot.EndMinutes, gridEndMinutes);
            if (end <= start) return null;

            var startRow = firstDataRow + (start - gridStartMinutes) / stepMinutes;
            var endRow = firstDataRow - 1
                + (int)Math.Ceiling((end - gridStartMinutes) / (double)stepMinutes);
            return endRow < startRow ? null : (startRow, endRow);
        }

        /// <summary>
        /// Splits placements in one column into those that can be merged into a clean block and
        /// those that overlap something else. Overlapping meetings cannot be merged — Excel
        /// rejects overlapping merges — and an overlap is a real finding (a double-booked room,
        /// or a faculty member scheduled in two places at once), so it must stay visible.
        /// </summary>
        internal static (List<T> Clear, List<T> Overlapping) SplitOverlaps<T>(
            IEnumerable<T> placements, Func<T, (int Start, int End)> span)
        {
            var all = placements.ToList();
            var claims = new Dictionary<int, int>();
            foreach (var p in all)
            {
                var (start, end) = span(p);
                for (var r = start; r <= end; r++) claims[r] = claims.GetValueOrDefault(r) + 1;
            }

            var clear = new List<T>();
            var overlapping = new List<T>();
            foreach (var p in all)
            {
                var (start, end) = span(p);
                var conflicted = Enumerable.Range(start, end - start + 1)
                    .Any(r => claims.GetValueOrDefault(r) > 1);
                (conflicted ? overlapping : clear).Add(p);
            }
            return (clear, overlapping);
        }
    }
}
