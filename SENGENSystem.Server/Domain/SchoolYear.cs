namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// An academic school year (e.g. "AY 2026-2027"). It groups the semesters that fall
    /// within it; exactly one school year is expected to be active at a time so the rest of
    /// the system has a single institutional calendar to key off (FR-DASH, FR-SCHED, data §5).
    /// </summary>
    public class SchoolYear
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty; // e.g. "AY 2026-2027"

        public bool IsActive { get; set; }

        public DateOnly StartDate { get; set; }

        public DateOnly EndDate { get; set; }

        /// <summary>The semesters filed under this school year.</summary>
        public ICollection<Semester> Semesters { get; set; } = new List<Semester>();
    }
}
