using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Publishing;

namespace SENGENSystem.Server.Features.Scheduling.Board
{
    /// <summary>
    /// FR-PUB-04: changing a class that is already published. Publication is not a status flag —
    /// it is a promise already emailed to a faculty member and to every student holding a seat.
    /// Moving that class afterwards is legitimate (rooms flood, staff change), but leaving those
    /// people on the old time is not. So an edit to a published row is recorded as an
    /// <b>amendment</b>: the row is flagged, the change is described in plain terms, and everyone
    /// who was told the old arrangement is told the new one.
    /// <para>
    /// Draft rows are untouched by any of this — they were never promised to anyone.
    /// </para>
    /// </summary>
    internal static class ScheduleAmendments
    {
        /// <summary>A human description of one placement, for before/after messages.</summary>
        public static string Describe(DayOfWeek day, int startMinutes, int endMinutes, string roomName) =>
            $"{day} {Hhmm(startMinutes)}–{Hhmm(endMinutes)} in {roomName}";

        public static string Hhmm(int minutes) => $"{minutes / 60:D2}:{minutes % 60:D2}";

        /// <summary>
        /// Flags a published row as amended and notifies its faculty member and every student with
        /// an approved seat in the section. <paramref name="change"/> is the sentence those people
        /// read, e.g. "moved from Monday 08:00–09:30 in Room 201 to Tuesday 10:00–11:30 in Room 301".
        /// Staged on the context — the caller's SaveChangesAsync commits it with the edit itself, so
        /// a class never moves without its notices moving with it.
        /// </summary>
        public static async Task RecordAsync(
            ScheduleAssignment assignment,
            string change,
            AppDbContext db,
            AuditLog audit,
            Notifier notifier,
            ClaimsPrincipal principal,
            CancellationToken ct)
        {
            if (!assignment.IsPublished) return;

            assignment.IsAmended = true;
            assignment.AmendedAtUtc = DateTime.UtcNow;
            assignment.AmendedByUserId = CurrentUserId(principal);

            var subjectCode = assignment.Section?.Subject?.Code ?? "A class";
            var cohort = assignment.Section is null
                ? string.Empty
                : $" ({assignment.Section.ProgramCode} {assignment.Section.YearLevel}-{assignment.Section.Block})";

            audit.Record(AuditAction.ScheduleAmended,
                $"Amended the published schedule: {subjectCode}{cohort} {change}.",
                "ScheduleAssignment", assignment.Id.ToString());

            var title = $"Schedule change: {subjectCode}";
            var message = $"{subjectCode}{cohort} {change}. Check My schedule for your updated week.";

            // The faculty member who was told they were teaching it.
            var facultyUserId = await db.FacultyProfiles.AsNoTracking()
                .Where(f => f.Id == assignment.FacultyProfileId)
                .Select(f => f.UserId)
                .FirstOrDefaultAsync(ct);
            if (facultyUserId != Guid.Empty)
            {
                notifier.Notify(facultyUserId, NotificationKind.ScheduleAmended, title, message, "/schedule");
            }

            // Every student holding an approved seat in the section — the ones whose week just moved.
            var studentUserIds = await db.SlotRequests.AsNoTracking()
                .Where(r => r.SectionId == assignment.SectionId
                    && r.Status == SlotRequestStatus.Approved
                    && r.StudentRegistration!.UserId != null)
                .Select(r => r.StudentRegistration!.UserId!.Value)
                .Distinct()
                .ToListAsync(ct);
            if (studentUserIds.Count > 0)
            {
                notifier.NotifyMany(studentUserIds, NotificationKind.ScheduleAmended, title, message, "/schedule");
            }
        }

        /// <summary>
        /// Emails the same amendment to the affected faculty member and students. Best-effort and
        /// deliberately called <i>after</i> the edit is committed: a mail outage must not roll back
        /// a schedule change the board has already accepted. Returns how many messages went out.
        /// </summary>
        public static async Task<int> EmailAsync(
            ScheduleAssignment assignment,
            string change,
            AppDbContext db,
            IEmailSender email,
            CancellationToken ct)
        {
            if (!assignment.IsPublished) return 0;

            var subjectCode = assignment.Section?.Subject?.Code ?? "A class";
            var subjectTitle = assignment.Section?.Subject?.Title ?? string.Empty;
            var cohort = assignment.Section is null
                ? string.Empty
                : $"{assignment.Section.ProgramCode} {assignment.Section.YearLevel}-{assignment.Section.Block}";

            var sent = 0;

            var faculty = await db.FacultyProfiles.AsNoTracking()
                .Include(f => f.User)
                .FirstOrDefaultAsync(f => f.Id == assignment.FacultyProfileId, ct);
            if (faculty?.User is { IsActive: true } facultyUser)
            {
                var (subject, body) = PublishingEmails.ScheduleAmended(
                    facultyUser.FullName, subjectCode, subjectTitle, cohort, change);
                var result = await email.SendAsync(facultyUser.Email, facultyUser.FullName, subject, body, ct);
                if (result.Sent) sent++;
            }

            var students = await db.SlotRequests.AsNoTracking()
                .Where(r => r.SectionId == assignment.SectionId && r.Status == SlotRequestStatus.Approved)
                .Select(r => r.StudentRegistration!)
                .ToListAsync(ct);
            foreach (var student in students.DistinctBy(s => s.Email))
            {
                var (subject, body) = PublishingEmails.ScheduleAmended(
                    student.FullName, subjectCode, subjectTitle, cohort, change);
                var result = await email.SendAsync(student.Email, student.FullName, subject, body, ct);
                if (result.Sent) sent++;
            }

            return sent;
        }

        private static Guid? CurrentUserId(ClaimsPrincipal principal) =>
            Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    }
}
