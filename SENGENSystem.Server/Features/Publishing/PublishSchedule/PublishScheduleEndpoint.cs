using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Publishing.PublishSchedule
{
    // Vertical slice: the Registrar publishes a semester's finalized, constraint-verified
    // schedule before the enrollment period opens (FR-PUB-01). Publishing flips
    // ScheduleAssignment.IsPublished — generation never replaces published rows — and
    // notifies affected faculty and confirmed students by email (FR-PUB-03).
    public record PublishScheduleResponse(
        Guid SemesterId,
        string SemesterName,
        int PublishedNow,
        int AlreadyPublished,
        int Total,
        int EmailsSent);

    public static class PublishScheduleEndpoint
    {
        public static IEndpointRouteBuilder MapPublishSchedule(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/publishing/{semesterId:guid}/publish", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.Registrar), nameof(UserRole.SchoolAdmin)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            Guid semesterId,
            AppDbContext db,
            AuditLog audit,
            Notifier notifier,
            Features.Reports.Live.ReportsBroadcaster broadcaster,
            IEmailSender email,
            CancellationToken cancellationToken)
        {
            var semester = await db.Semesters.FirstOrDefaultAsync(s => s.Id == semesterId, cancellationToken);
            if (semester is null)
            {
                return Results.NotFound(new { message = "Semester not found." });
            }

            if (semester.IsArchived)
            {
                return Results.Conflict(new { message = $"“{semester.Name}” is archived — its schedule is read-only." });
            }

            var assignments = await db.ScheduleAssignments
                .Where(a => a.SemesterId == semester.Id)
                .Include(a => a.FacultyProfile).ThenInclude(f => f!.User)
                .ToListAsync(cancellationToken);

            if (assignments.Count == 0)
            {
                return Results.BadRequest(new
                {
                    message = "There is no schedule to publish for this semester yet. Generate or build one first."
                });
            }

            var drafts = assignments.Where(a => !a.IsPublished).ToList();
            var alreadyPublished = assignments.Count - drafts.Count;
            if (drafts.Count == 0)
            {
                return Results.Ok(new PublishScheduleResponse(
                    semester.Id, semester.Name, 0, alreadyPublished, assignments.Count, 0));
            }

            foreach (var assignment in drafts)
            {
                assignment.IsPublished = true;
            }

            audit.Record(AuditAction.SchedulePublished,
                $"Published {drafts.Count} schedule assignment(s) for {semester.Name}.",
                "Semester", semester.Id.ToString());

            // Recipients: every faculty member on the schedule, and confirmed students —
            // in-app bell notices for those with accounts, email for everyone reachable.
            var facultyRecipients = assignments
                .Select(a => a.FacultyProfile?.User)
                .Where(u => u is not null && u.IsActive)
                .DistinctBy(u => u!.Id)
                .ToList();

            var studentRecipients = await db.StudentRegistrations
                .Where(r => r.SemesterId == semester.Id && r.Status == RegistrationStatus.Confirmed)
                .ToListAsync(cancellationToken);

            // Bell notices commit in the same transaction as the publish itself.
            foreach (var user in facultyRecipients)
            {
                var classCount = assignments.Count(a => a.FacultyProfile?.UserId == user!.Id);
                notifier.Notify(user!.Id, NotificationKind.SchedulePublished,
                    "Your teaching schedule is published",
                    $"{classCount} class(es) for {semester.Name} are now final. Open My schedule to see your week.",
                    "/schedule");
            }
            notifier.NotifyMany(
                studentRecipients.Where(r => r.UserId is not null).Select(r => r.UserId!.Value),
                NotificationKind.SchedulePublished,
                "Class schedules are published",
                $"The {semester.Name} timetable is now official. Your approved subjects appear in My schedule.",
                "/schedule");

            await db.SaveChangesAsync(cancellationToken);
            broadcaster.Announce("publishing");

            // Publication emails are best-effort: the publish itself is already committed.
            // Faculty are notified about their whole published schedule, not just the delta.
            var emailsSent = 0;

            foreach (var user in facultyRecipients)
            {
                var classCount = assignments.Count(a => a.FacultyProfile?.UserId == user!.Id);
                var (subject, body) = PublishingEmails.FacultySchedulePublished(user!, semester.Name, classCount);
                var result = await email.SendAsync(user!.Email, user.FullName, subject, body, cancellationToken);
                if (result.Sent) emailsSent++;
            }

            foreach (var registration in studentRecipients.DistinctBy(r => r.Email))
            {
                var (subject, body) = PublishingEmails.StudentSchedulePublished(registration, semester.Name);
                var result = await email.SendAsync(registration.Email, registration.FullName, subject, body, cancellationToken);
                if (result.Sent) emailsSent++;
            }

            if (emailsSent > 0)
            {
                audit.Record(AuditAction.NotificationDispatched,
                    $"Sent schedule publication notices for {semester.Name} to {emailsSent} recipient(s).",
                    "Semester", semester.Id.ToString());
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.Ok(new PublishScheduleResponse(
                semester.Id, semester.Name, drafts.Count, alreadyPublished, assignments.Count, emailsSent));
        }
    }
}
