namespace SENGENSystem.Server.Common.Notifications
{
    /// <summary>
    /// Sends transactional email for lifecycle events (FR-NOTIF-01). Implementations must not
    /// throw for delivery failures that the caller should treat as non-fatal — they return a
    /// result the caller can log and audit (FR-NOTIF-02).
    /// </summary>
    public interface IEmailSender
    {
        Task<EmailResult> SendAsync(
            string toEmail, string toName, string subject, string htmlBody, CancellationToken cancellationToken);
    }

    /// <summary>Outcome of an email dispatch attempt.</summary>
    public readonly record struct EmailResult(bool Sent, string Detail)
    {
        public static EmailResult Ok(string detail) => new(true, detail);
        public static EmailResult Failed(string detail) => new(false, detail);
    }
}
