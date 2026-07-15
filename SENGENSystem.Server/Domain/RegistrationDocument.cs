namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// One admission-requirement paper and its submission state for a given enrollee
    /// (FR-DOC-01/02). One row is created per <see cref="DocumentType"/> when a
    /// <see cref="StudentRegistration"/> is submitted; school personnel update the status later.
    /// </summary>
    public class RegistrationDocument
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid StudentRegistrationId { get; set; }

        public StudentRegistration? StudentRegistration { get; set; }

        public DocumentType DocumentType { get; set; }

        public DocumentStatus Status { get; set; } = DocumentStatus.NotSubmitted;
    }
}
