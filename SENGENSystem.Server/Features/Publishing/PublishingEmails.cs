using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Publishing
{
    /// <summary>
    /// Builds the HTML bodies for schedule-publication notices (FR-PUB-03, FR-NOTIF-01).
    /// Mirrors <c>RegistrationEmails</c> so the STI voice and layout stay consistent.
    /// </summary>
    internal static class PublishingEmails
    {

        public static (string Subject, string Body) FacultySchedulePublished(User faculty, string semesterName, int classCount) =>
            ($"Class Schedule Published — {semesterName}",
             EmailLayout.Wrap(
                $"<h2>Your teaching schedule is out</h2>" +
                $"<p>Hi {EmailLayout.Escape(faculty.FirstName)},</p>" +
                $"<p>The official class schedule for <strong>{EmailLayout.Escape(semesterName)}</strong> has been published " +
                $"by the Registrar.</p>" +
                $"<p>You have <strong>{classCount}</strong> assigned class meeting{(classCount == 1 ? "" : "s")} this term. " +
                $"Open <strong>My schedule</strong> in SEN-GEN to view your weekly timetable.</p>"));

        /// <summary>
        /// FR-PUB-04: a class that was already published has moved. Sent to the faculty member and
        /// to every student holding a seat — the same people the original publication notice
        /// reached, because they are the ones now holding a wrong time.
        /// </summary>
        public static (string Subject, string Body) ScheduleAmended(
            string recipientName, string subjectCode, string subjectTitle, string cohort, string change) =>
            ($"Schedule Change — {subjectCode}",
             EmailLayout.Wrap(
                $"<h2>A published class has changed</h2>" +
                $"<p>Hi {EmailLayout.Escape(FirstNameOf(recipientName))},</p>" +
                $"<p><strong>{EmailLayout.Escape(subjectCode)}</strong>{(string.IsNullOrWhiteSpace(subjectTitle) ? string.Empty : $" — {EmailLayout.Escape(subjectTitle)}")}" +
                $"{(string.IsNullOrWhiteSpace(cohort) ? string.Empty : $" ({EmailLayout.Escape(cohort)})")} " +
                $"{EmailLayout.Escape(change)}.</p>" +
                $"<p>This class was already published, so please update any copy of your timetable. " +
                $"Open <strong>My schedule</strong> in SEN-GEN for the current week.</p>"));

        private static string FirstNameOf(string fullName) =>
            string.IsNullOrWhiteSpace(fullName) ? "there" : fullName.Split(' ')[0];

        public static (string Subject, string Body) StudentSchedulePublished(StudentRegistration r, string semesterName) =>
            ($"Class Schedules Now Available — {semesterName}",
             EmailLayout.Wrap(
                $"<h2>Class schedules are published</h2>" +
                $"<p>Hi {EmailLayout.Escape(r.FirstName)},</p>" +
                $"<p>Class schedules for <strong>{EmailLayout.Escape(semesterName)}</strong> are now published.</p>" +
                $"<p><strong>Student number:</strong> {r.StudentNumber}</p>" +
                $"<p>Sign in to SEN-GEN to browse the published sections — subjects, times, rooms, and " +
                $"faculty — and proceed with your subject enlistment.</p>"));
    }
}
