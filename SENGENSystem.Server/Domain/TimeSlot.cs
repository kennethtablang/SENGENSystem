namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// An allowable meeting slot (a system parameter). Two slots conflict when they fall on
    /// the same day and their minute ranges overlap (FR-SCHED-05 allowable time slots).
    /// </summary>
    public class TimeSlot
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public DayOfWeek Day { get; set; }

        /// <summary>Start time expressed as minutes from midnight (e.g. 480 = 08:00).</summary>
        public int StartMinutes { get; set; }

        /// <summary>End time expressed as minutes from midnight (e.g. 570 = 09:30).</summary>
        public int EndMinutes { get; set; }

        /// <summary>True when this slot and <paramref name="other"/> share a day and overlap in time.</summary>
        public bool OverlapsWith(TimeSlot other) =>
            Day == other.Day && StartMinutes < other.EndMinutes && other.StartMinutes < EndMinutes;
    }
}
