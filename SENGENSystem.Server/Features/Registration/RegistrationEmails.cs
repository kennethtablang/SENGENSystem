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
                $"registration number for enrollment transactions and term activation:</p>" +
                $"<p style=\"font-size:20px;font-weight:700;letter-spacing:1px;color:#0072BC\">{r.StudentNumber}</p>" +
                $"<p><strong>Program:</strong> {r.Program}<br>" +
                $"<strong>Type:</strong> {Humanize(r.StudentType.ToString())}</p>" +
                $"<p>Your official student number is issued separately by the school's student system. " +
                $"Our Registrar will review your submission and admission requirements, and you will be " +
                $"contacted if anything further is needed.</p>"));

        /// <summary>
        /// Sent once SEN-GEN provisions a login for the student from their SIS details. The
        /// temporary password only ever reaches the mailbox the student gave, and the account is
        /// flagged to force a password change on the first sign-in.
        /// </summary>
        public static (string Subject, string Body) AccountCredentials(
            StudentRegistration r, string loginEmail, string temporaryPassword) =>
            ($"Your SEN-GEN Account — {r.StudentNumber}",
             Wrap(
                $"<h2>Your account is ready</h2>" +
                $"<p>Hi {Escape(r.FirstName)},</p>" +
                $"<p>We've created a SEN-GEN account for you from your Student Information Sheet so " +
                $"you can sign in, track your admission documents, and select your subject slots.</p>" +
                $"<p><strong>Sign in with:</strong></p>" +
                $"<table style=\"border-collapse:collapse;margin:8px 0\">" +
                $"<tr><td style=\"padding:4px 12px 4px 0;color:#555\">Email</td>" +
                $"<td style=\"padding:4px 0;font-weight:600\">{Escape(loginEmail)}</td></tr>" +
                $"<tr><td style=\"padding:4px 12px 4px 0;color:#555\">Temporary password</td>" +
                $"<td style=\"padding:4px 0;font-family:monospace;font-size:16px;font-weight:700;" +
                $"letter-spacing:1px;color:#0072BC\">{Escape(temporaryPassword)}</td></tr></table>" +
                $"<p>For your security you'll be asked to <strong>set your own password the first time " +
                $"you sign in</strong>. Please don't share this email.</p>"));

        /// <summary>
        /// Receipt sent the moment a returning student <i>requests</i> term activation — proof the
        /// request was filed, distinct from <see cref="TermActivationConfirmation"/> which is sent
        /// only once the Admission Officer validates it.
        /// </summary>
        public static (string Subject, string Body) TermActivationRequested(StudentRegistration r, string semesterName) =>
            ($"Term Activation Requested — {r.StudentNumber}",
             Wrap(
                $"<h2>Term activation request received</h2>" +
                $"<p>Hi {Escape(r.FirstName)},</p>" +
                $"<p>We've received your request to activate your enrollment for " +
                $"<strong>{Escape(semesterName)}</strong>. Keep this email as your receipt and proof " +
                $"that the request was filed.</p>" +
                $"<p><strong>Registration number:</strong> {r.StudentNumber}</p>" +
                $"<p>Your request is now <strong>pending review</strong> by our Admission Officer. " +
                $"You'll receive another email once it has been validated — there's no need to re-submit " +
                $"your SIS.</p>"));

        public static (string Subject, string Body) TermActivationConfirmation(StudentRegistration r, string semesterName) =>
            ($"Term Activation Confirmed — {r.StudentNumber}",
             Wrap(
                $"<h2>Term activation confirmed</h2>" +
                $"<p>Hi {Escape(r.FirstName)},</p>" +
                $"<p>Your enrollment has been activated for <strong>{Escape(semesterName)}</strong>.</p>" +
                $"<p><strong>Registration number:</strong> {r.StudentNumber}</p>" +
                (string.IsNullOrWhiteSpace(r.OfficialStudentNumber)
                    ? string.Empty
                    : $"<p><strong>Student number:</strong> {Escape(r.OfficialStudentNumber!)}</p>") +
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
