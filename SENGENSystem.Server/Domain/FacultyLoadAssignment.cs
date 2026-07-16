namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// A teaching-load allocation: a faculty member is assigned to teach a subject to a specific
    /// class section (student block) in a given semester (FR-FAC-01). A (subject, class section)
    /// pair is taught by at most one faculty member. The total assigned units per faculty per
    /// semester is checked against <see cref="FacultyProfile.MaxLoadUnits"/> (FR-FAC-03) and feeds
    /// schedule generation.
    /// </summary>
    public class FacultyLoadAssignment
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid FacultyProfileId { get; set; }

        public FacultyProfile? FacultyProfile { get; set; }

        public Guid SubjectId { get; set; }

        public Subject? Subject { get; set; }

        /// <summary>The class section (student block) this subject is taught to.</summary>
        public Guid ClassSectionId { get; set; }

        public ClassSection? ClassSection { get; set; }

        public Guid SemesterId { get; set; }

        public Semester? Semester { get; set; }

        public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
