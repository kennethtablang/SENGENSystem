namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// A faculty member's preferred teaching window (FR-SCHED-03). Soft input to the CSP
    /// engine: placements inside a preferred window are rewarded, outside are penalized —
    /// never at the cost of a hard constraint.
    /// </summary>
    public class FacultyTimePreference
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid FacultyProfileId { get; set; }

        public FacultyProfile? FacultyProfile { get; set; }

        public DayOfWeek Day { get; set; }

        /// <summary>Window start, minutes from midnight (e.g. 480 = 08:00).</summary>
        public int StartMinutes { get; set; }

        /// <summary>Window end, minutes from midnight (e.g. 720 = 12:00).</summary>
        public int EndMinutes { get; set; }
    }
}
