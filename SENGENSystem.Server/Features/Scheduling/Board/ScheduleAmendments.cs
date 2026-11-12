using SENGENSystem.Server.Common.Formatting;
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
            $"{day} {ClockText.Hhmm(startMinutes)}–{ClockText.Hhmm(endMinutes)} in {roomName}";


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
        /// Queues the same amendment notice for the affected faculty member and every student
        /// holding a seat, on the outbox. Returns how many were queued.
        /// <para>
        /// These used to be sent inline — one SMTP call per recipient, up to a section's forty
        /// students plus its teacher, inside the request that moved the class. Dragging a published
        /// class on the board therefore waited on the mail server, and a failed notice was logged
        /// and forgotten: the same shape F-07 and F-12 moved onto the outbox, on a path that was
        /// missed. Now the notices commit as rows and the dispatcher delivers them, with retries
        /// and a record of every failure on the Outbox page.
        /// </para>
        /// <para>
        /// The caller commits the queued rows in their own save after the edit's save, so the
        /// edit still stands whatever happens to its notices — the property the inline version
        /// had, kept.
        /// </para>
        /// </summary>
        public static async Task<int> QueueNoticesAsync(
            ScheduleAssignment assignment,
            string change,
            AppDbContext db,
            EmailOutbox outbox,
            CancellationToken ct)
        {
            if (!assignment.IsPublished) return 0;

            var subjectCode = assignment.Section?.Subject?.Code ?? "A class";
            var subjectTitle = assignment.Section?.Subject?.Title ?? string.Empty;
            var cohort = assignment.Section is null
                ? string.Empty
                : $"{assignment.Section.ProgramCode} {assignment.Section.YearLevel}-{assignment.Section.Block}";

            var queued = 0;

            var faculty = await db.FacultyProfiles.AsNoTracking()
                .Include(f => f.User)
                .FirstOrDefaultAsync(f => f.Id == assignment.FacultyProfileId, ct);
            if (faculty?.User is { IsActive: true } facultyUser)
            {
                var (subject, body) = PublishingEmails.ScheduleAmended(
                    facultyUser.FullName, subjectCode, subjectTitle, cohort, change);
                if (outbox.Queue(facultyUser.Email, facultyUser.FullName, subject, body, kind: "ScheduleAmended"))
                {
                    queued++;
                }
            }

            var students = await db.SlotRequests.AsNoTracking()
                .Where(r => r.SectionId == assignment.SectionId && r.Status == SlotRequestStatus.Approved)
                .Select(r => r.StudentRegistration!)
                .ToListAsync(ct);
            foreach (var student in students.DistinctBy(s => s.Email))
            {
                var (subject, body) = PublishingEmails.ScheduleAmended(
                    student.FullName, subjectCode, subjectTitle, cohort, change);
                if (outbox.Queue(student.Email, student.FullName, subject, body, kind: "ScheduleAmended"))
                {
                    queued++;
                }
            }

            if (queued > 0)
            {
                await db.SaveChangesAsync(ct);
            }
            return queued;
        }

        private static Guid? CurrentUserId(ClaimsPrincipal principal) =>
            Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    }
}
