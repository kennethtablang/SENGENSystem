namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// The single collection window for the ISO/IEC 25010 rating survey. Responses keep being
    /// recorded while <see cref="IsOpen"/> — the Super Admin closes the window once satisfied with
    /// the number gathered, which stops new submissions without touching what was already collected.
    /// Closing is reversible: reopening resumes collection under the same instrument.
    /// One row only, identified by <see cref="SingletonId"/>.
    /// </summary>
    public class SurveyCampaign
    {
        /// <summary>Fixed key of the one-and-only campaign row.</summary>
        public static readonly Guid SingletonId = new("5e2f0a10-0000-4000-8000-000000000001");

        public Guid Id { get; set; } = SingletonId;

        public bool IsOpen { get; set; } = true;

        /// <summary>How many responses the Super Admin is aiming for; drives the progress readout.</summary>
        public int TargetResponses { get; set; } = 30;

        public DateTime OpenedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? ClosedAtUtc { get; set; }

        /// <summary>Who last opened or closed the window, for the audit trail shown on the dashboard.</summary>
        public string LastChangedBy { get; set; } = string.Empty;
    }
}
