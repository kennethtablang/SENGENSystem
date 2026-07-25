namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// An emailed invitation for one user to answer the ISO/IEC 25010 rating survey (FR-AUTH,
    /// evaluation). The Super Admin dispatches these; the raw token travels only in the emailed link
    /// while the database keeps its hash, so a leaked row can't open someone else's survey. One live
    /// invitation per user; answering it records the linked <see cref="SurveyResponse"/>.
    /// </summary>
    public class SurveyInvitation
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }

        public User? User { get; set; }

        // Snapshot of who was invited, so results read even if the account later changes.
        public string RecipientName { get; set; } = string.Empty;

        public string RecipientEmail { get; set; } = string.Empty;

        public string RecipientRole { get; set; } = string.Empty;

        /// <summary>SHA-256 hash of the opaque token in the emailed link.</summary>
        public string TokenHash { get; set; } = string.Empty;

        public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAtUtc { get; set; }

        public SurveyResponse? Response { get; set; }
    }
}
