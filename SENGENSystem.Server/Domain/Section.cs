namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// An offering of a <see cref="Subject"/> for a semester that students enlist into. The
    /// (ProgramCode, YearLevel, Block) triple identifies the student cohort: sections sharing
    /// a cohort must never overlap in time (FR-SCHED-02). Capacity is capped by
    /// <see cref="SystemSettings.SectionCapacityCap"/> (FR-ENL-03).
    /// </summary>
    public class Section
    {
        /// <summary>
        /// The out-of-the-box cap (FR-ENL-03). This is the seed value for
        /// <see cref="SystemSettings.SectionCapacityCap"/> and the fallback used before the
        /// settings row exists — the stored setting, not this constant, is the live authority.
        /// </summary>
        public const int DefaultCapacityCap = 40;

        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid SubjectId { get; set; }

        public Subject? Subject { get; set; }

        public Guid SemesterId { get; set; }

        public Semester? Semester { get; set; }

        public string SectionCode { get; set; } = string.Empty; // e.g. "BSCS-1A-CS101"

        public string ProgramCode { get; set; } = string.Empty;

        public int YearLevel { get; set; }

        public string Block { get; set; } = string.Empty; // e.g. "A"

        public int Capacity { get; set; } = DefaultCapacityCap;

        /// <summary>
        /// Approved-enlistment seat count (FR-ENL-02/03). Guarded by a DB CHECK constraint
        /// (never above <see cref="Capacity"/>) and optimistic concurrency via <see cref="RowVersion"/>.
        /// </summary>
        public int EnrolledCount { get; set; }

        /// <summary>Concurrency token so racing approvals cannot oversell the last seat.</summary>
        public byte[] RowVersion { get; set; } = [];

        /// <summary>Identifies the student cohort for time-overlap constraints.</summary>
        public string CohortKey => $"{ProgramCode}-{YearLevel}-{Block}";
    }
}
