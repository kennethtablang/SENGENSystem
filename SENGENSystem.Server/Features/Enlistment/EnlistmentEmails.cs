using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Enlistment
{
    /// <summary>
    /// Builds the HTML bodies for slot-approval workflow emails (FR-ENL-04, FR-NOTIF-01).
    /// Mirrors <c>RegistrationEmails</c> so the STI voice and layout stay consistent.
    /// </summary>
    internal static class EnlistmentEmails
    {

        public static (string Subject, string Body) SlotApproved(
            StudentRegistration r, string subjectCode, string subjectTitle, string sectionCode) =>
            ($"Slot Approved: {subjectCode} — {r.StudentNumber}",
             EmailLayout.Wrap(
                $"<h2>Your seat is confirmed</h2>" +
                $"<p>Hi {EmailLayout.Escape(r.FirstName)},</p>" +
                $"<p>The Registrar has approved your slot request:</p>" +
                $"<p><strong>{EmailLayout.Escape(subjectCode)}</strong> — {EmailLayout.Escape(subjectTitle)}<br>" +
                $"<strong>Section:</strong> {EmailLayout.Escape(sectionCode)}</p>" +
                $"<p>The class now appears on <strong>My schedule</strong> in SEN-GEN.</p>"));

        public static (string Subject, string Body) SlotRejected(
            StudentRegistration r, string subjectCode, string subjectTitle, string sectionCode, string? reason) =>
            ($"Slot Request Update: {subjectCode} — {r.StudentNumber}",
             EmailLayout.Wrap(
                $"<h2>About your slot request</h2>" +
                $"<p>Hi {EmailLayout.Escape(r.FirstName)},</p>" +
                $"<p>Your slot request could not be approved:</p>" +
                $"<p><strong>{EmailLayout.Escape(subjectCode)}</strong> — {EmailLayout.Escape(subjectTitle)}<br>" +
                $"<strong>Section:</strong> {EmailLayout.Escape(sectionCode)}</p>" +
                (string.IsNullOrWhiteSpace(reason)
                    ? ""
                    : $"<p><strong>Registrar's note:</strong> {EmailLayout.Escape(reason)}</p>") +
                $"<p>You may request a different section in SEN-GEN, or visit the Registrar for assistance.</p>"));
    }
}
