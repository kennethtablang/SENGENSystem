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

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>The programs this requirement applies to. Empty means it applies to none.</summary>
        public ICollection<AdmissionRequirementProgram> Programs { get; set; }
            = new List<AdmissionRequirementProgram>();
    }
}
