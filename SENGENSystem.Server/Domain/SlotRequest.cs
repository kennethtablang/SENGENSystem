namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// A student's request for a seat in a <see cref="Section"/> (FR-ENL). Routed through the
    /// Registrar's approval workflow (FR-ENL-04); an approval consumes one seat of the
    /// section's 40-slot capacity (FR-ENL-03).
    /// </summary>
    public class SlotRequest
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid StudentRegistrationId { get; set; }

        public StudentRegistration? StudentRegistration { get; set; }

        public Guid SectionId { get; set; }

        public Section? Section { get; set; }

        public SlotRequestStatus Status { get; set; } = SlotRequestStatus.Requested;

        public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? DecidedAtUtc { get; set; }

        /// <summary>The Registrar who approved/rejected, when decided.</summary>
        public Guid? DecidedByUserId { get; set; }

        public string? RejectionReason { get; set; }
    }
}
