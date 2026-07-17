namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// An academic term. The dashboard and scheduling are semester-aware; exactly one
    /// semester is expected to be active at a time (FR-DASH-01, FR-SCHED-05, data §5).
    /// </summary>
    public class Semester
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty; // auto-derived, e.g. "AY 2026-2027 — First Semester"

        /// <summary>Which of the two terms of its school year this is (hard-coded first/second).</summary>
        public SemesterTerm Term { get; set; } = SemesterTerm.FirstSemester;

        public bool IsActive { get; set; }

        /// <summary>
        /// Set once the term is finished: the semester's schedule becomes a frozen, read-only
        /// archive — the board, generator, and publisher refuse further changes to it.
        /// </summary>
        public bool IsArchived { get; set; }

        public DateTime? ArchivedAtUtc { get; set; }

        public DateOnly StartDate { get; set; }

        public DateOnly EndDate { get; set; }

        /// <summary>The school year this semester belongs to (FR: School Year contains Semesters).</summary>
        public Guid? SchoolYearId { get; set; }

        public SchoolYear? SchoolYear { get; set; }
    }
}
