using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Auth
{
    /// <summary>
    /// HTML bodies for account-security emails (password reset, email-change confirmation).
    /// Mirrors <c>PublishingEmails</c>/<c>RegistrationEmails</c> so the STI voice stays consistent.
    /// </summary>
    internal static class AccountEmails
    {
        private const string Brand = "STI College Alaminos — SEN-GEN";

        public static (string Subject, string Body) PasswordReset(User user, string link, int validMinutes) =>
            ("Reset Your SEN-GEN Password",
             Wrap(
                "<h2>Password reset requested</h2>" +
                $"<p>Hi {Escape(user.FirstName)},</p>" +
                "<p>We received a request to reset the password of your SEN-GEN account. " +
                "Click the button below to choose a new password.</p>" +
                Button(link, "Reset my password") +
                $"<p>This link is valid for <strong>{validMinutes} minutes</strong> and can be used once. " +
                "If you did not request this, you can safely ignore this email — your password stays unchanged.</p>"));

        public static (string Subject, string Body) ConfirmEmailChange(User user, string newEmail, string link, int validHours) =>
            ("Confirm Your New SEN-GEN Email Address",
             Wrap(
                "<h2>Confirm your new email address</h2>" +
                $"<p>Hi {Escape(user.FirstName)},</p>" +
                $"<p>You asked to change your SEN-GEN sign-in email to <strong>{Escape(newEmail)}</strong>. " +
                "To complete the change, confirm that you own this mailbox:</p>" +
                Button(link, "Confirm email change") +
                $"<p>This link is valid for <strong>{validHours} hours</strong>. Until you confirm, you keep " +
                "signing in with your current address. If you did not request this, ignore this email.</p>"));

        private static string Button(string href, string label) =>
            $"<p style=\"margin:20px 0\"><a href=\"{href}\" " +
            "style=\"background:#FFD700;color:#003399;font-weight:bold;text-decoration:none;" +
            $"padding:12px 26px;border-radius:999px;display:inline-block\">{label}</a></p>" +
            $"<p style=\"font-size:12px;color:#888\">Or open this link: {href}</p>";

        private static string Wrap(string inner) =>
            "<div style=\"font-family:Arial,Helvetica,sans-serif;color:#1a1a1a;line-height:1.5\">" +
            inner +
            "<hr style=\"border:none;border-top:1px solid #e5e5e5;margin:24px 0\">" +
            $"<p style=\"font-size:12px;color:#888\">{Brand}. This is an automated message — please do not reply.</p>" +
            "</div>";

        private static string Escape(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
