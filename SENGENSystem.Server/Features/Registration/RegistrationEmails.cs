using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Registration
{
    /// <summary>
    /// Builds the HTML bodies for registration lifecycle emails (FR-SIS-05, FR-NOTIF-01).
    /// Kept together so the STI voice and layout stay consistent across notices.
    /// </summary>
    internal static class RegistrationEmails
    {
        private const string Brand = "STI College Alaminos — SEN-GEN";

        public static (string Subject, string Body) RegistrationConfirmation(StudentRegistration r) =>
            ($"SIS Registration Received — {r.StudentNumber}",
             Wrap(
                $"<h2>Registration received</h2>" +
                $"<p>Hi {Escape(r.FirstName)},</p>" +
                $"<p>Your Student Information Sheet has been submitted successfully. Please keep your " +
                $"student number for enrollment transactions and term activation:</p>" +
                $"<p style=\"font-size:20px;font-weight:700;letter-spacing:1px;color:#0072BC\">{r.StudentNumber}</p>" +
                $"<p><strong>Program:</strong> {r.Program}<br>" +
                $"<strong>Type:</strong> {Humanize(r.StudentType.ToString())}</p>" +
                $"<p>Our Registrar will review your submission and admission requirements. " +
                $"You will be contacted if anything further is needed.</p>"));

        public static (string Subject, string Body) TermActivationConfirmation(StudentRegistration r, string semesterName) =>
            ($"Term Activation Confirmed — {r.StudentNumber}",
             Wrap(
                $"<h2>Term activation confirmed</h2>" +
                $"<p>Hi {Escape(r.FirstName)},</p>" +
                $"<p>Your enrollment has been activated for <strong>{Escape(semesterName)}</strong>.</p>" +
                $"<p><strong>Student number:</strong> {r.StudentNumber}</p>" +
                $"<p>You may now proceed with the next steps of enrollment for this term.</p>"));

        private static string Wrap(string inner) =>
            "<div style=\"font-family:Arial,Helvetica,sans-serif;color:#1a1a1a;line-height:1.5\">" +
            inner +
            $"<hr style=\"border:none;border-top:1px solid #e5e5e5;margin:24px 0\">" +
            $"<p style=\"font-size:12px;color:#888\">{Brand}. This is an automated message — please do not reply.</p>" +
            "</div>";

        // "NewStudent" -> "New student"
        private static string Humanize(string pascal)
        {
            var spaced = System.Text.RegularExpressions.Regex.Replace(pascal, "([a-z])([A-Z])", "$1 $2");
            return spaced.Length == 0 ? spaced : char.ToUpperInvariant(spaced[0]) + spaced[1..].ToLowerInvariant();
        }

        private static string Escape(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
