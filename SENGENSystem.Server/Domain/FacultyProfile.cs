namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// Scheduling profile for a Faculty Member user: load ceiling and department (used to
    /// determine which subjects the member is eligible to teach). Load limits are a hard
    /// constraint; department drives eligibility (FR-FAC-01/03, FR-SCHED-02/05).
    /// </summary>
    public class FacultyProfile
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>The <see cref="User"/> (role FacultyMember) this profile belongs to.</summary>
        public Guid UserId { get; set; }

        public User? User { get; set; }

        /// <summary>Program/department the member teaches for; matched against <see cref="Subject.ProgramCode"/>.</summary>
        public string ProgramCode { get; set; } = string.Empty;

        /// <summary>Maximum teaching load in subject units (FR-FAC-03, FR-SCHED-02).</summary>
        public int MaxLoadUnits { get; set; } = 24;

        /// <summary>Institutional employee number shown on load reports (searchable).</summary>
        public string EmployeeId { get; set; } = string.Empty;
    }
}
