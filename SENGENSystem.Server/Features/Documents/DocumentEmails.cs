using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Documents
{
    /// <summary>
    /// Builds the HTML bodies for document-checklist emails (FR-DOC-05, FR-NOTIF-01).
    /// Mirrors <c>RegistrationEmails</c> so the STI voice and layout stay consistent.
    /// </summary>
    internal static class DocumentEmails
    {
        private const string Brand = "STI College Alaminos — SEN-GEN";

        public static (string Subject, string Body) SubmissionReminder(
            StudentRegistration r, IReadOnlyList<string> missing) =>
            ($"Admission Requirements Reminder — {r.StudentNumber}",
             Wrap(
                $"<h2>Some admission requirements are still missing</h2>" +
                $"<p>Hi {Escape(r.FirstName)},</p>" +
                $"<p>Our records show your admission checklist is not yet complete. " +
                $"Please submit the following to the Admission Office:</p>" +
                "<ul>" +
                string.Concat(missing.Select(m => $"<li>{Escape(m)}</li>")) +
                "</ul>" +
                $"<p><strong>Student number:</strong> {r.StudentNumber}</p>" +
                $"<p>Completing your requirements keeps your enrollment on track — incomplete " +
                $"checklists cannot be cleared for subject enlistment.</p>"));

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
