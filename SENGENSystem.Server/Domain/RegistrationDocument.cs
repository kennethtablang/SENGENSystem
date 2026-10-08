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

        /// <summary>
        /// When this row's status was last decided, and by whom. FR-DOC-03 calls the checklist "an
        /// auditable record", and until these existed the only trace of a decision was a free-text
        /// audit entry that could not be joined back to the row — so the board could show a paper as
        /// received with nothing on the row itself saying who had said so, or when.
        /// <para>
        /// Both stay null for a freshly seeded checklist, which is the honest reading: nobody has
        /// decided anything about that paper yet. They are set together, on every path that changes
        /// <see cref="Status"/>.
        /// </para>
        /// </summary>
        public DateTime? UpdatedAtUtc { get; set; }

        public Guid? VerifiedByUserId { get; set; }
    }
}
