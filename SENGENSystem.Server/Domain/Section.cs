namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// An offering of a <see cref="Subject"/> for a semester that students enlist into. The
    /// (ProgramCode, YearLevel, Block) triple identifies the student cohort: sections sharing
    /// a cohort must never overlap in time (FR-SCHED-02). Capacity is capped at 40 (FR-ENL-03).
    /// </summary>
    public class Section
    {
        public const int MaxCapacity = 40;

        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid SubjectId { get; set; }

        public Subject? Subject { get; set; }

        public Guid SemesterId { get; set; }

        public Semester? Semester { get; set; }

        public string SectionCode { get; set; } = string.Empty; // e.g. "BSCS-1A-CS101"

        public string ProgramCode { get; set; } = string.Empty;

        public int YearLevel { get; set; }

        public string Block { get; set; } = string.Empty; // e.g. "A"

        public int Capacity { get; set; } = MaxCapacity;

        /// <summary>Identifies the student cohort for time-overlap constraints.</summary>
        public string CohortKey => $"{ProgramCode}-{YearLevel}-{Block}";
    }
}
