using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Publishing
{
    /// <summary>
    /// Builds the HTML bodies for schedule-publication notices (FR-PUB-03, FR-NOTIF-01).
    /// Mirrors <c>RegistrationEmails</c> so the STI voice and layout stay consistent.
    /// </summary>
    internal static class PublishingEmails
    {
        private const string Brand = "STI College Alaminos — SEN-GEN";

        public static (string Subject, string Body) FacultySchedulePublished(User faculty, string semesterName, int classCount) =>
            ($"Class Schedule Published — {semesterName}",
             Wrap(
                $"<h2>Your teaching schedule is out</h2>" +
                $"<p>Hi {Escape(faculty.FirstName)},</p>" +
                $"<p>The official class schedule for <strong>{Escape(semesterName)}</strong> has been published " +
                $"by the Registrar.</p>" +
                $"<p>You have <strong>{classCount}</strong> assigned class meeting{(classCount == 1 ? "" : "s")} this term. " +
                $"Open <strong>My schedule</strong> in SEN-GEN to view your weekly timetable.</p>"));

        public static (string Subject, string Body) StudentSchedulePublished(StudentRegistration r, string semesterName) =>
            ($"Class Schedules Now Available — {semesterName}",
             Wrap(
                $"<h2>Class schedules are published</h2>" +
                $"<p>Hi {Escape(r.FirstName)},</p>" +
                $"<p>Class schedules for <strong>{Escape(semesterName)}</strong> are now published.</p>" +
                $"<p><strong>Student number:</strong> {r.StudentNumber}</p>" +
                $"<p>Sign in to SEN-GEN to browse the published sections — subjects, times, rooms, and " +
                $"faculty — and proceed with your subject enlistment.</p>"));

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
