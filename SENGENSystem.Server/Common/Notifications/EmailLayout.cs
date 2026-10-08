namespace SENGENSystem.Server.Common.Notifications
{
    /// <summary>
    /// The one HTML frame every SEN-GEN email is sent in, and the one escaper its builders use.
    /// Before this, each of the five email builders carried its own copy of both, plus its own
    /// hard-coded "STI College Alaminos — SEN-GEN" footer — so the institution name configured on
    /// System parameters reached every printed document but none of the mail.
    /// <para>
    /// <b>The footer names the institution at send time, not build time.</b> Builders are static and
    /// have no database; queued mail can sit in the outbox for a while; and every message, inline
    /// or queued, leaves through <see cref="SmtpEmailSender"/>. So <see cref="Wrap"/> writes
    /// <see cref="InstitutionSlot"/> into the footer and the sender fills it with
    /// <see cref="Brand"/> just before handing the message to SMTP. Outbox bodies are never shown
    /// to a user, so the slot is never seen unfilled.
    /// </para>
    /// </summary>
    public static class EmailLayout
    {
        /// <summary>Stands in for the institution name until <see cref="Brand"/> fills it.</summary>
        public const string InstitutionSlot = "{{SENGEN_INSTITUTION}}";

        public static string Wrap(string inner) =>
            "<div style=\"font-family:Arial,Helvetica,sans-serif;color:#1a1a1a;line-height:1.5\">" +
            inner +
            "<hr style=\"border:none;border-top:1px solid #e5e5e5;margin:24px 0\">" +
            $"<p style=\"font-size:12px;color:#888\">{InstitutionSlot} — SEN-GEN. " +
            "This is an automated message — please do not reply.</p>" +
            "</div>";

        /// <summary>Fills the footer's institution slot. Bodies without the slot pass through untouched.</summary>
        public static string Brand(string htmlBody, string institutionName) =>
            htmlBody.Replace(InstitutionSlot, Escape(
                string.IsNullOrWhiteSpace(institutionName)
                    ? Domain.SystemSettings.DefaultInstitutionName
                    : institutionName.Trim()));

        public static string Escape(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
