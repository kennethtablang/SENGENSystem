namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// A configurable admission-requirement definition (FR-DOC-01). Replaces the former fixed
    /// <c>DocumentType</c> enum: school personnel add, rename, and archive requirements on the
    /// requirements page, and choose which programs (courses) each one applies to. When a student
    /// submits a SIS, one <see cref="RegistrationDocument"/> is seeded per active requirement whose
    /// <see cref="Programs"/> include the enrollee's <see cref="StudentRegistration.Program"/>.
    /// </summary>
    public class AdmissionRequirement
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Stable short code stored on each <see cref="RegistrationDocument.RequirementCode"/>. The
        /// nine built-in requirements keep the old enum names ("HepaA", …) so historical checklist
        /// rows still resolve. Runtime-added requirements get a generated code.
        /// </summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Display name shown on the checklist, e.g. "Hepatitis A Vaccination Record".</summary>
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        /// <summary>Archived requirements (false) are no longer seeded onto new checklists.</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>Presentation order on the checklist and manage screens.</summary>
        public int SortOrder { get; set; }

        /// <summary>
        /// Whether a brand-new enrollee is asked for this paper. Report cards and permanent
        /// records come from the high school a <see cref="StudentType.NewStudent"/> is leaving,
        /// so they are never asked of a transferee (FR-DOC-01).
        /// </summary>
        public bool AppliesToNewStudents { get; set; } = true;

        /// <summary>
        /// Whether a <see cref="StudentType.Transferee"/> is asked for this paper. The transcript
        /// and honorable dismissal come from the college they are leaving, so they are never asked
        /// of a new student.
        /// </summary>
        public bool AppliesToTransferees { get; set; } = true;

        /// <summary>
        /// Whether the Admission Officer must have this paper in hand before the enrollee can be
        /// pre-authorized for online slot selection (FR-PRE-02). The rest of the checklist may
        /// still be arriving — those are followed up on, not blocked on.
        /// </summary>
        public bool IsRequiredForAuthorization { get; set; }

        /// <summary>
        /// Whether a Certificate of Grades is accepted in lieu of the paper itself. When set, the
        /// checklist offers <see cref="DocumentStatus.CertificateOfGrades"/> in place of
        /// <see cref="DocumentStatus.XeroxCopy"/> — the Official Transcript of Records is only
        /// ever accepted as an original or against a certificate of grades, never as a photocopy.
        /// </summary>
        public bool AcceptsCertificateOfGrades { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>The programs this requirement applies to. Empty means it applies to none.</summary>
        public ICollection<AdmissionRequirementProgram> Programs { get; set; }
            = new List<AdmissionRequirementProgram>();
    }
}
