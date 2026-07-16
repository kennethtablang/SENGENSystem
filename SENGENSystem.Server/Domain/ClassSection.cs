namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// A student class/block defined at setup time for a specific semester (term): a course (program)
    /// at a year level, split into a named section (e.g. BSCS · Year 3 · "A"). Classes are created
    /// afresh each semester. Unlike <see cref="Section"/> — a per-subject scheduling offering — a
    /// class section is the reusable cohort a curriculum's subjects are delivered to. The
    /// (SemesterId, ProgramCode, YearLevel, SectionName) tuple is unique.
    /// </summary>
    public class ClassSection
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>The semester (term) this class is offered in; classes are created per semester.</summary>
        public Guid SemesterId { get; set; }

        public Semester? Semester { get; set; }

        /// <summary>Program/course this class belongs to; matched against <see cref="Curriculum.ProgramCode"/>.</summary>
        public string ProgramCode { get; set; } = string.Empty; // e.g. "BSCS"

        public int YearLevel { get; set; } // e.g. 3

        /// <summary>The section label within the (program, year) cohort.</summary>
        public string SectionName { get; set; } = string.Empty; // e.g. "A"

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Human-readable cohort label, e.g. "BSCS 3-A".</summary>
        public string DisplayName => $"{ProgramCode} {YearLevel}-{SectionName}";
    }
}
