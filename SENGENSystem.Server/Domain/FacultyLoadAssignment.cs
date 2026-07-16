namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// A teaching-load allocation: a faculty member is assigned to teach a subject in a given
    /// semester (FR-FAC-01). The total assigned units per faculty per semester is checked against
    /// <see cref="FacultyProfile.MaxLoadUnits"/> (FR-FAC-03) and feeds schedule generation.
    /// </summary>
    public class FacultyLoadAssignment
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid FacultyProfileId { get; set; }

        public FacultyProfile? FacultyProfile { get; set; }

        public Guid SubjectId { get; set; }

        public Subject? Subject { get; set; }

        public Guid SemesterId { get; set; }

        public Semester? Semester { get; set; }

        public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
