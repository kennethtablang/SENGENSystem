using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Enlistment
{
    /// <summary>
    /// Builds the HTML bodies for slot-approval workflow emails (FR-ENL-04, FR-NOTIF-01).
    /// Mirrors <c>RegistrationEmails</c> so the STI voice and layout stay consistent.
    /// </summary>
    internal static class EnlistmentEmails
    {
        private const string Brand = "STI College Alaminos — SEN-GEN";

        public static (string Subject, string Body) SlotApproved(
            StudentRegistration r, string subjectCode, string subjectTitle, string sectionCode) =>
            ($"Slot Approved: {subjectCode} — {r.StudentNumber}",
             Wrap(
                $"<h2>Your seat is confirmed</h2>" +
                $"<p>Hi {Escape(r.FirstName)},</p>" +
                $"<p>The Registrar has approved your slot request:</p>" +
                $"<p><strong>{Escape(subjectCode)}</strong> — {Escape(subjectTitle)}<br>" +
                $"<strong>Section:</strong> {Escape(sectionCode)}</p>" +
                $"<p>The class now appears on <strong>My schedule</strong> in SEN-GEN.</p>"));

        public static (string Subject, string Body) SlotRejected(
            StudentRegistration r, string subjectCode, string subjectTitle, string sectionCode, string? reason) =>
            ($"Slot Request Update: {subjectCode} — {r.StudentNumber}",
             Wrap(
                $"<h2>About your slot request</h2>" +
                $"<p>Hi {Escape(r.FirstName)},</p>" +
                $"<p>Your slot request could not be approved:</p>" +
                $"<p><strong>{Escape(subjectCode)}</strong> — {Escape(subjectTitle)}<br>" +
                $"<strong>Section:</strong> {Escape(sectionCode)}</p>" +
                (string.IsNullOrWhiteSpace(reason)
                    ? ""
                    : $"<p><strong>Registrar's note:</strong> {Escape(reason)}</p>") +
                $"<p>You may request a different section in SEN-GEN, or visit the Registrar for assistance.</p>"));

        private static string Wrap(string inner) =>
            "<div style=\"font-family:Arial,Helvetica,sans-serif;color:#1a1a1a;line-height:1.5\">" +
            inner +
            $"<hr style=\"border:none;border-top:1px solid #e5e5e5;margin:24px 0\">" +
            $"<p style=\"font-size:12px;color:#888\">{Brand}. This is an automated message — please do not reply.</p>" +
            "</div>";

        private static string Escape(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
