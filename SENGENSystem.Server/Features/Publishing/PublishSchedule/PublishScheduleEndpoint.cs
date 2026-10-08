using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Scheduling;

namespace SENGENSystem.Server.Features.Publishing.PublishSchedule
{
    // Vertical slice: the Registrar publishes a semester's finalized, constraint-verified
    // schedule before the enrollment period opens (FR-PUB-01). Only a finalized draft can be
    // published (F-15); the GET preview is the dry run the confirmation dialog reads. Publishing flips
    // ScheduleAssignment.IsPublished — generation never replaces published rows — and
    // notifies affected faculty and confirmed students by email (FR-PUB-03).
    public record PublishScheduleResponse(
        Guid SemesterId,
        string SemesterName,
        int PublishedNow,
        int AlreadyPublished,
        int Total,
        // Accepted for sending, not delivered — the notices are queued and go out in the background.
        int EmailsQueued);

    // The dry run behind the Publish button's confirmation (F-15 / the bulk-path confirmation):
    // what one press would do, computed by the same code the publish itself uses, so the number
    // the Registrar agrees to is the number that happens. Nothing is written.
    public record PublishPreviewResponse(
        Guid SemesterId,
        string SemesterName,
        int ToPublish,
        int AlreadyPublished,
        // Draft rows not yet signed off by the Academic Head — any of these blocks the publish.
        int NotFinalized,
        int FacultyToNotify,
        int StudentsToNotify,
        // Null when the publish would go through; otherwise the sentence the publish refuses with.
        string? BlockedReason);

    public static class PublishScheduleEndpoint
    {
        public static IEndpointRouteBuilder MapPublishSchedule(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/publishing/{semesterId:guid}/publish", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.Registrar), nameof(UserRole.SchoolAdmin)));
            app.MapGet("/api/publishing/{semesterId:guid}/preview", PreviewAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.Registrar), nameof(UserRole.SchoolAdmin)));
            return app;
        }

        // F-15: the lifecycle is strictly Draft → Finalized → Published. Publishing a draft the
        // Academic Head never signed off would make official — and announce to the whole
        // institution — a timetable still open to regeneration and board edits.
        internal static string? BlockedReason(int toPublish, int notFinalized) =>
            toPublish > 0 && notFinalized > 0
                ? $"{notFinalized} of the {toPublish} draft class(es) have not been finalized. " +
                  "The Academic Head must finalize the schedule before it can be published."
                : null;

        private static async Task<List<StudentRegistration>> StudentRecipientsAsync(
            AppDbContext db, Guid semesterId, CancellationToken ct) =>
            await db.StudentRegistrations
                .Where(r => r.SemesterId == semesterId && r.Status == RegistrationStatus.Confirmed)
                .ToListAsync(ct);

        private static List<User> FacultyRecipients(IEnumerable<ScheduleAssignment> assignments) =>
            assignments
                .Select(a => a.FacultyProfile?.User)
                .Where(u => u is not null && u.IsActive)
                .DistinctBy(u => u!.Id)
                .Select(u => u!)
                .ToList();

        private static async Task<IResult> PreviewAsync(
            Guid semesterId, AppDbContext db, CancellationToken ct)
        {
            var semester = await db.Semesters.AsNoTracking().FirstOrDefaultAsync(s => s.Id == semesterId, ct);
            if (semester is null)
            {
                return Results.NotFound(new { message = "Semester not found." });
            }

            var assignments = await db.ScheduleAssignments.AsNoTracking()
                .Where(a => a.SemesterId == semester.Id)
                .Include(a => a.FacultyProfile).ThenInclude(f => f!.User)
                .ToListAsync(ct);

            var drafts = assignments.Where(a => !a.IsPublished).ToList();
            var notFinalized = drafts.Count(a => !a.IsFinalized);
            var students = await StudentRecipientsAsync(db, semester.Id, ct);

            var blocked = semester.IsArchived
                ? $"“{semester.Name}” is archived — its schedule is read-only."
                : assignments.Count == 0
                    ? "There is no schedule to publish for this semester yet."
                    : BlockedReason(drafts.Count, notFinalized);

            return Results.Ok(new PublishPreviewResponse(
                semester.Id, semester.Name, drafts.Count, assignments.Count - drafts.Count, notFinalized,
                drafts.Count == 0 ? 0 : FacultyRecipients(assignments).Count,
                drafts.Count == 0 ? 0 : students.DistinctBy(r => r.Email).Count(),
                blocked));
        }

        private static async Task<IResult> HandleAsync(
            Guid semesterId,
            AppDbContext db,
            AuditLog audit,
            Notifier notifier,
            Features.Reports.Live.ReportsBroadcaster broadcaster,
            EmailOutbox outbox,
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

            if (BlockedReason(drafts.Count, drafts.Count(a => !a.IsFinalized)) is { } blocked)
            {
                return Results.Conflict(new { message = blocked });
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
            var facultyRecipients = FacultyRecipients(assignments);
            var studentRecipients = await StudentRecipientsAsync(db, semester.Id, cancellationToken);

            // Bell notices commit in the same transaction as the publish itself.
            foreach (var user in facultyRecipients)
            {
                var classCount = assignments.Count(a => a.FacultyProfile?.UserId == user.Id);
                notifier.Notify(user.Id, NotificationKind.SchedulePublished,
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

            // The assignments were read at the top of this handler; if a regenerate replaced them in
            // between, publishing would make official a timetable nobody has seen — and, since the
            // emails below follow the commit, announce it to every faculty member and confirmed
            // student before anyone noticed. The concurrency token turns that into a 409. Wrapping
            // this save rather than an earlier one keeps the publish, its audit entry, and its bell
            // notices in one transaction, exactly as before.
            if (await ScheduleConcurrency.TrySaveAsync(db, "publish this schedule", cancellationToken)
                is { } conflict)
            {
                return conflict;
            }
            broadcaster.Announce("publishing");

            // Publication notices go through the outbox, for the same reason the reminder sweep and
            // the bulk approval do — and this is the path where the cost of not doing so was
            // actually observed. One press of Publish sent every faculty member and every confirmed
            // student their notice synchronously, inside the request: an announcement to the whole
            // institution, unbounded, unrecallable, and with the caller's browser holding the
            // connection open until the mail server had finished with it.
            //
            // Queueing does not make an accidental publish recoverable — the rows are committed and
            // the mail is on its way — but it does bound the request, make every recipient visible
            // in one table, and record a failure instead of swallowing it.
            //
            // Keyed per semester and recipient, so publishing a term that is already partly
            // published cannot tell the same person twice about the same timetable.
            var queued = 0;
            foreach (var user in facultyRecipients)
            {
                var classCount = assignments.Count(a => a.FacultyProfile?.UserId == user.Id);
                var (subject, body) = PublishingEmails.FacultySchedulePublished(user, semester.Name, classCount);
                if (outbox.Queue(user.Email, user.FullName, subject, body,
                        kind: "SchedulePublished",
                        dedupeKey: $"published:{semester.Id}:faculty:{user.Id}"))
                {
                    queued++;
                }
            }

            foreach (var registration in studentRecipients.DistinctBy(r => r.Email))
            {
                var (subject, body) = PublishingEmails.StudentSchedulePublished(registration, semester.Name);
                if (outbox.Queue(registration.Email, registration.FullName, subject, body,
                        kind: "SchedulePublished",
                        dedupeKey: $"published:{semester.Id}:student:{registration.Id}"))
                {
                    queued++;
                }
            }

            if (queued > 0)
            {
                audit.Record(AuditAction.NotificationDispatched,
                    $"Queued schedule publication notices for {semester.Name} to {queued} recipient(s).",
                    "Semester", semester.Id.ToString());
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.Ok(new PublishScheduleResponse(
                semester.Id, semester.Name, drafts.Count, alreadyPublished, assignments.Count, queued));
        }
    }
}
