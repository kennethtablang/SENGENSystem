namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// A curriculum subject/course. Units drive faculty-load accounting and the program/year
    /// place it within a fixed curriculum cohort (FR-SCHED-04, FR-SCHED-05, data §5).
    /// </summary>
    public class Subject
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Code { get; set; } = string.Empty;  // e.g. "CS101"

        public string Title { get; set; } = string.Empty;

        public int Units { get; set; }

        public string ProgramCode { get; set; } = string.Empty; // e.g. "BSCS"

        public int YearLevel { get; set; }

        /// <summary>Requires a laboratory room; enforced as a hard placement constraint.</summary>
        public bool RequiresLaboratory { get; set; }
    }
}
