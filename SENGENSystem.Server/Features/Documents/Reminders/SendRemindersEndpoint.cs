using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Documents.Reminders
{
    // Vertical slice: automated document-submission reminder emails for incomplete checklists
    // (FR-DOC-05, FR-NOTIF-01). Staff trigger a sweep (or a single student's reminder); each
    // email lists exactly the papers still missing.
    public record SendRemindersRequest(Guid? RegistrationId);

    /// <summary>
    /// <paramref name="Queued"/> replaces the old "EmailsSent": the sweep now hands the mail to the
    /// outbox and returns, so the honest answer at response time is how many were accepted for
    /// sending, not how many arrived. <paramref name="SkippedRecentlyReminded"/> and
    /// <paramref name="Remaining"/> exist so a bounded sweep can say what it did and what is left
    /// — an officer who presses the button and is told "0 queued" deserves to know whether that
    /// means "nobody needs chasing" or "everybody was chased an hour ago".
    /// </summary>
    public record SendRemindersResponse(
        int Targeted, int Queued, int SkippedRecentlyReminded, int Remaining);

    public static class SendRemindersEndpoint
    {
        public static IEndpointRouteBuilder MapDocumentReminders(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/documents/reminders", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.AdmissionOfficer), nameof(UserRole.Registrar), nameof(UserRole.SchoolAdmin)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            SendRemindersRequest request,
            AppDbContext db,
            AuditLog audit,
            EmailOutbox outbox,
            CancellationToken cancellationToken)
        {
            var query = db.StudentRegistrations
                .Include(r => r.Documents)
                .Where(r => r.Status != RegistrationStatus.Rejected
                    && r.Documents.Any(d => d.Status == DocumentStatus.NotSubmitted));

            if (request.RegistrationId is { } id)
            {
                // Chasing one named enrollee is deliberate — honour it whatever term they are in.
                query = query.Where(r => r.Id == id);
            }
            else if (await db.GetActiveSemesterIdAsync(cancellationToken) is { } activeSemesterId)
            {
                // A blanket sweep only ever chases the current term. Without this, every rollover
                // widens the blast: students who finished terms ago would be emailed about papers
                // for an enrollment that is long closed.
                query = query.Where(r => r.SemesterId == activeSemesterId);
            }

            var isSingle = request.RegistrationId is not null;
            var now = DateTime.UtcNow;

            // Ordering matters once the sweep is capped: oldest-chased first, so repeated presses
            // work through the queue instead of re-offering the same 200 enrollees every time.
            // Nulls sort first under SQL Server's ordering, which is what we want — nobody ever
            // reminded is the most overdue there is.
            var targets = await query
                .OrderBy(r => r.LastRemindedAtUtc)
                .ThenBy(r => r.Id)
                .ToListAsync(cancellationToken);

            if (isSingle && targets.Count == 0)
            {
                return Results.BadRequest(new
                {
                    message = "This enrollee's checklist is already complete (or the record was not found)."
                });
            }

            // A named single-student reminder bypasses the quiet period. Chasing one person on
            // purpose is a deliberate act by someone looking at their record; the interval exists to
            // stop the blanket sweep, not to stop an officer doing their job.
            var eligible = isSingle
                ? targets
                : targets.Where(r => ReminderPolicy.MayRemind(r, now)).ToList();
            var skipped = targets.Count - eligible.Count;

            var batch = eligible.Take(ReminderPolicy.BatchCap).ToList();
            var remaining = eligible.Count - batch.Count;

            var catalog = await DocumentChecklist.LoadCatalogAsync(db, cancellationToken);

            var queued = 0;
            foreach (var registration in batch)
            {
                // Only chase papers this enrollee's student type is actually asked for, so a
                // transferee is never reminded about a Form 138 (FR-DOC-01/05).
                var missing = DocumentChecklist.Applicable(registration, catalog)
                    .Where(d => d.Status == DocumentStatus.NotSubmitted)
                    .OrderBy(d => catalog.Order(d.RequirementCode))
                    .Select(d => catalog.Label(d.RequirementCode))
                    .ToList();
                if (missing.Count == 0) continue;

                var (subject, body) = DocumentEmails.SubmissionReminder(registration, missing);
                // Queued rather than sent: the mail commits with this transaction and goes out on
                // the dispatcher's next cycle, so a term-sized sweep no longer runs inside the
                // request. The dedupe key is the second guard behind the interval check above.
                if (outbox.Queue(registration.Email, registration.FullName, subject, body,
                        kind: "DocumentReminder",
                        dedupeKey: ReminderPolicy.DedupeKey(registration, now)))
                {
                    registration.LastRemindedAtUtc = now;
                    queued++;
                }
            }

            if (queued > 0)
            {
                audit.Record(AuditAction.NotificationDispatched,
                    $"Queued document submission reminder(s) for {queued} enrollee(s) with incomplete checklists"
                    + (skipped > 0 ? $"; {skipped} skipped as recently reminded" : string.Empty)
                    + (remaining > 0 ? $"; {remaining} still to chase." : "."));
            }

            // One save commits the queued mail, the reminded-at stamps, and the audit entry
            // together — so a failure here sends nothing, and a success loses nothing.
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new SendRemindersResponse(targets.Count, queued, skipped, remaining));
        }
    }
}
