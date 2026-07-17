namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// An in-app notice on a user's bell (FR-NOTIF). Complements — never replaces — the
    /// email notices: emails also reach account-less registrants, while these rows only
    /// exist for signed-in users.
    /// </summary>
    public class Notification
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }

        public User? User { get; set; }

        public NotificationKind Kind { get; set; } = NotificationKind.General;

        public string Title { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;

        /// <summary>Client route the notice links to (e.g. "/schedule"), if any.</summary>
        public string? LinkTo { get; set; }

        public bool IsRead { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
