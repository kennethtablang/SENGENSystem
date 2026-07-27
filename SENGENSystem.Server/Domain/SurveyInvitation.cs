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

        /// <summary>When the Super Admin last pushed an in-app bell notice for this invitation.</summary>
        public DateTime? NotifiedAtUtc { get; set; }

        /// <summary>How many follow-up nudges were sent after the first dispatch.</summary>
        public int ReminderCount { get; set; }

        /// <summary>Optional personal note the Super Admin attached when inviting this person.</summary>
        public string? Note { get; set; }

        /// <summary>Who dispatched the invitation, for the audit trail on the recipients page.</summary>
        public string InvitedBy { get; set; } = string.Empty;

        public SurveyResponse? Response { get; set; }
    }
}
