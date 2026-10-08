using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Documents
{
    /// <summary>
    /// Builds the HTML bodies for document-checklist emails (FR-DOC-05, FR-NOTIF-01).
    /// Mirrors <c>RegistrationEmails</c> so the STI voice and layout stay consistent.
    /// </summary>
    internal static class DocumentEmails
    {
        /// <summary>
        /// <paramref name="blocking"/> is the subset of <paramref name="missing"/> that gates
        /// pre-authorization (and so enlistment). Only those are described as holding the student
        /// up: the reminder used to tell everyone that any missing paper blocked enlistment, which
        /// was untrue for the rest of the checklist and sent students queueing for papers that
        /// could have followed later.
        /// </summary>
        public static (string Subject, string Body) SubmissionReminder(
            StudentRegistration r, IReadOnlyList<string> missing, IReadOnlyCollection<string> blocking) =>
            ($"Admission Requirements Reminder — {r.StudentNumber}",
             EmailLayout.Wrap(
                $"<h2>Some admission requirements are still missing</h2>" +
                $"<p>Hi {EmailLayout.Escape(r.FirstName)},</p>" +
                $"<p>Our records show your admission checklist is not yet complete. " +
                $"Please bring the following to the Admission Office:</p>" +
                "<ul>" +
                string.Concat(missing.Select(m => blocking.Contains(m)
                    ? $"<li><strong>{EmailLayout.Escape(m)}</strong> — needed before you can enlist</li>"
                    : $"<li>{EmailLayout.Escape(m)}</li>")) +
                "</ul>" +
                $"<p><strong>Student number:</strong> {r.StudentNumber}</p>" +
                (blocking.Count > 0
                    ? "<p>The papers marked above must be in before the Admission Office can clear you " +
                      "for subject enlistment. The rest can follow, but are still required.</p>"
                    : "<p>None of these hold up your subject enlistment, but they are still required " +
                      "to complete your admission.</p>")));
    }
}
