namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// One admission-requirement paper and its submission state for a given enrollee
    /// (FR-DOC-01/02). One row is created per applicable <see cref="AdmissionRequirement"/> when a
    /// <see cref="StudentRegistration"/> is submitted; school personnel update the status later.
    /// The requirement is referenced loosely by <see cref="RequirementCode"/> so archiving a
    /// requirement never orphans historical checklists.
    /// </summary>
    public class RegistrationDocument
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid StudentRegistrationId { get; set; }

        public StudentRegistration? StudentRegistration { get; set; }

        /// <summary>The <see cref="AdmissionRequirement.Code"/> of the paper this row tracks.</summary>
        public string RequirementCode { get; set; } = string.Empty;

        public DocumentStatus Status { get; set; } = DocumentStatus.NotSubmitted;
    }
}
