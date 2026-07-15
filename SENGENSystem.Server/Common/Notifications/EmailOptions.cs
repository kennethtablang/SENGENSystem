namespace SENGENSystem.Server.Common.Notifications
{
    /// <summary>
    /// SMTP configuration for outgoing notifications (FR-NOTIF). Non-secret values live in
    /// <c>appsettings.json</c> under "Email"; <see cref="Password"/> is supplied only via
    /// user-secrets / environment (never a tracked file).
    /// </summary>
    public sealed class EmailOptions
    {
        public const string SectionName = "Email";

        public string Host { get; set; } = "smtp.gmail.com";

        public int Port { get; set; } = 587;

        /// <summary>SMTP account used to authenticate (also the envelope sender).</summary>
        public string User { get; set; } = string.Empty;

        /// <summary>App password / SMTP secret. Injected from user-secrets, not appsettings.</summary>
        public string Password { get; set; } = string.Empty;

        public string FromAddress { get; set; } = string.Empty;

        public string FromName { get; set; } = "STI.SEN-GEN";

        /// <summary>When true (no credentials configured), emails are logged instead of sent.</summary>
        public bool IsConfigured => !string.IsNullOrWhiteSpace(User) && !string.IsNullOrWhiteSpace(Password);
    }
}
