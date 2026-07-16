namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// A curriculum subject/course. Units drive faculty-load accounting and the program/year
    /// place it within a fixed curriculum cohort (FR-SCHED-04, FR-SCHED-05, data §5).
    /// </summary>
    public class Subject
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Code { get; set; } = string.Empty;  // e.g. "CS101"

        public string Title { get; set; } = string.Empty;

        public int Units { get; set; }

        /// <summary>
        /// Weekly contact hours that must be plotted on the schedule board. Distinct from
        /// <see cref="Units"/> (e.g. a 1-unit laboratory meets 3 hours a week). The Weekly Hours
        /// Tracker compares this against the hours actually placed on the calendar.
        /// </summary>
        public int Hours { get; set; }

        public string ProgramCode { get; set; } = string.Empty; // e.g. "BSCS" (kept in sync with the curriculum's program)

        public int YearLevel { get; set; }

        /// <summary>The term (first/second semester) this subject is offered in within its year.</summary>
        public SemesterTerm Term { get; set; } = SemesterTerm.FirstSemester;

        /// <summary>Requires a laboratory room; enforced as a hard placement constraint.</summary>
        public bool RequiresLaboratory { get; set; }

        /// <summary>The curriculum this subject belongs to.</summary>
        public Guid? CurriculumId { get; set; }

        public Curriculum? Curriculum { get; set; }

        /// <summary>The subjects that must be taken before this one (same curriculum).</summary>
        public ICollection<SubjectPrerequisite> Prerequisites { get; set; } = new List<SubjectPrerequisite>();
    }
}
