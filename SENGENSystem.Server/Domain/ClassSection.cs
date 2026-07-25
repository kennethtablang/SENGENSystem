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

        /// <summary>
        /// The curriculum this cohort follows. Distinct cohorts of the same program can sit on
        /// different curriculum versions at once — a 2nd-year block on the retired catalog while a
        /// 1st-year block starts the new one — so the curriculum is chosen per class section, not
        /// inferred from the program's single active catalog. Drives which subjects the cohort is
        /// offered (faculty load) and lets schedule generation accept those subjects even after the
        /// old catalog is archived. Nullable for rows created before this field existed
        /// (backfilled to the program's active curriculum on startup).
        /// </summary>
        public Guid? CurriculumId { get; set; }

        public Curriculum? Curriculum { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Human-readable cohort label, e.g. "BSCS 3-A".</summary>
        public string DisplayName => $"{ProgramCode} {YearLevel}-{SectionName}";
    }
}
