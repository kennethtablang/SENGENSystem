namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// A program curriculum — a versioned catalog of subjects for a program of study
    /// (e.g. "BSIT" effective 2024). Multiple effectivity years can coexist per program;
    /// the active version is the current catalog. Subjects belong to exactly one curriculum
    /// (FR-SCHED-04 curriculum-aware scheduling, data §5).
    /// </summary>
    public class Curriculum
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string ProgramCode { get; set; } = string.Empty; // e.g. "BSIT"

        public string ProgramName { get; set; } = string.Empty; // e.g. "BS Information Technology"

        public bool IsActive { get; set; }

        /// <summary>
        /// Retired curricula stay in the database with their subjects and history intact,
        /// but leave the active catalog. Curricula are archived, never deleted.
        /// </summary>
        public bool IsArchived { get; set; }

        public DateTime? ArchivedAtUtc { get; set; }

        public string? ArchiveReason { get; set; }

        /// <summary>The subjects that make up this curriculum, across year levels.</summary>
        public ICollection<Subject> Subjects { get; set; } = new List<Subject>();

        /// <summary>The school years this curriculum is effective for (checkbox effectivity).</summary>
        public ICollection<CurriculumSchoolYear> SchoolYears { get; set; } = new List<CurriculumSchoolYear>();
    }
}
