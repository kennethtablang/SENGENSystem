namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// Links an <see cref="AdmissionRequirement"/> to a program (course) it applies to. A student in
    /// a program is asked for exactly the active requirements that list that program here — so, for
    /// example, ITP enrollees skip the health papers that only HRS/HRA require.
    /// </summary>
    public class AdmissionRequirementProgram
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid AdmissionRequirementId { get; set; }

        public AdmissionRequirement? AdmissionRequirement { get; set; }

        public ProgramTrack Program { get; set; }
    }
}
