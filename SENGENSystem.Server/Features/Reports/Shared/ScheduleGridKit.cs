using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Reports.Shared
{
    /// <summary>
    /// Shared machinery for the timetable-style Excel reports. The grids differ in what their
    /// columns mean — rooms for the room grid, weekdays for the faculty grid — but they agree
    /// on how a time slot maps to rows, how blocks are coloured, and how an overlap is
    /// detected. Keeping that here stops the three grids from drifting apart.
    /// </summary>
    internal static class ScheduleGridKit
    {
        /// <summary>
        /// Block tints, chosen to stay legible behind dark text in print. Keyed off the subject
        /// code so one subject keeps one colour across every grid in the workbook.
        /// </summary>
        internal static readonly string[] BlockTints =
        [
            "#DCE9FB", "#E8F5E9", "#FDF0D5", "#F3E4F6", "#E0F2F4",
            "#FCE9E4", "#EDE7F6", "#E9F5E9", "#FFF3E0", "#E1F5FE"
        ];

        /// <summary>Stable colour index for a subject code — same subject, same colour, always.</summary>
        internal static int TintIndex(string? subjectCode)
        {
            if (string.IsNullOrEmpty(subjectCode)) return 0;
            var hash = subjectCode.Aggregate(17, (acc, c) => acc * 31 + c);
            return Math.Abs(hash) % BlockTints.Length;
        }

        internal static string Hhmm(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

        internal static string TimeRange(TimeSlot slot) =>
            $"{Hhmm(slot.StartMinutes)}–{Hhmm(slot.EndMinutes)}";

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
