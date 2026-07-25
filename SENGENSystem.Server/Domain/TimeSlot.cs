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

        /// <summary>
        /// Whether this is an admin-configured <b>allowable</b> meeting slot — part of the grid the
        /// CSP engine and the System Parameters page work from (FR-SCHED-05). False marks a synthetic
        /// assignment period the scheduler or the manual board created to persist one placement (an
        /// exact-duration block like 60/120/180 min). Only allowable slots feed the engine's grid, so
        /// those placement periods never dilute the configured allowable times.
        /// </summary>
        public bool IsAllowable { get; set; } = true;

        /// <summary>True when this slot and <paramref name="other"/> share a day and overlap in time.</summary>
        public bool OverlapsWith(TimeSlot other) =>
            Day == other.Day && StartMinutes < other.EndMinutes && other.StartMinutes < EndMinutes;
    }
}
