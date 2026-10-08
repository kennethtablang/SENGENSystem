using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Paging;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Notifications.Outbox
{
    // Vertical slice: the email outbox, made visible.
    //
    // Moving bulk mail onto an outbox fixed the half of the problem that was about *timing* — the
    // work left the request. It also started recording something that had never been recorded
    // before: a delivery that failed. But a record nobody can read is barely better than the
    // best-effort swallow it replaced, so this is the other half. "Did that student get the email?"
    // is the question the whole thing exists to answer, and until this page there was no way to ask
    // it short of querying the table by hand.

    public record OutboxRowDto(
        Guid Id,
        string ToEmail,
        string ToName,
        string Subject,
        string Kind,
        string Status,
        int Attempts,
        string? LastError,
        string CreatedAtUtc,
        string? SentAtUtc,
        string? NextAttemptAtUtc);

    public static class OutboxEndpoints
    {
        public static IEndpointRouteBuilder MapEmailOutbox(this IEndpointRouteBuilder app)
        {
            // Operational rather than academic: this is about whether the system is delivering, not
            // about any student's record, so it sits with the admins rather than the Registrar.
            var group = app.MapGroup("/api/admin/outbox")
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.SchoolAdmin), nameof(UserRole.SuperAdmin)));

            group.MapGet("", ListAsync);
            group.MapPost("{id:guid}/retry", RetryAsync);
            group.MapPost("retry-failed", RetryAllFailedAsync);
            return app;
        }

        private static async Task<IResult> ListAsync(
            string? status, string? search, int? page, int? pageSize, string? sort, string? dir,
            AppDbContext db, CancellationToken ct)
        {
            var query = db.OutboxEmails.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(m => m.ToEmail.Contains(term) || m.Subject.Contains(term));
            }

            // Counted before the status chip narrows it, so switching the view cannot change what
            // the page reports the backlog to be — same discipline as every other queue here.
            var baseQuery = query;

            if (!string.IsNullOrWhiteSpace(status)
                && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<OutboxEmailStatus>(status, ignoreCase: true, out var wanted))
            {
                query = query.Where(m => m.Status == wanted);
            }

            var desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            var ordered = (sort?.ToLowerInvariant()) switch
            {
                "toemail" => desc ? query.OrderByDescending(m => m.ToEmail) : query.OrderBy(m => m.ToEmail),
                "subject" => desc ? query.OrderByDescending(m => m.Subject) : query.OrderBy(m => m.Subject),
                "kind" => desc ? query.OrderByDescending(m => m.Kind) : query.OrderBy(m => m.Kind),
                "status" => desc ? query.OrderByDescending(m => m.Status) : query.OrderBy(m => m.Status),
                "attempts" => desc ? query.OrderByDescending(m => m.Attempts) : query.OrderBy(m => m.Attempts),
                // Newest first by default: an operational log is read from the top.
                _ => desc ? query.OrderBy(m => m.CreatedAtUtc) : query.OrderByDescending(m => m.CreatedAtUtc)
            };

            var paged = await ordered.ThenBy(m => m.Id).ToPagedAsync(PageSpec.From(page, pageSize), ct);

            var body = paged
                .Select(m => new OutboxRowDto(
                    m.Id, m.ToEmail, m.ToName, m.Subject, m.Kind,
                    m.Status.ToString(), m.Attempts, m.LastError,
                    Iso(m.CreatedAtUtc)!, Iso(m.SentAtUtc), Iso(m.NextAttemptAtUtc)))
                .ToResponse("emails");

            body["pendingCount"] = await baseQuery.CountAsync(m => m.Status == OutboxEmailStatus.Pending, ct);
            body["sentCount"] = await baseQuery.CountAsync(m => m.Status == OutboxEmailStatus.Sent, ct);
            body["failedCount"] = await baseQuery.CountAsync(m => m.Status == OutboxEmailStatus.Failed, ct);
            return Results.Ok(body);
        }

        // POST /api/admin/outbox/{id}/retry — put a given-up email back in the queue.
        private static async Task<IResult> RetryAsync(
            Guid id, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var mail = await db.OutboxEmails.FirstOrDefaultAsync(m => m.Id == id, ct);
            if (mail is null)
            {
                return Results.NotFound(new { message = "That email is no longer in the outbox." });
            }
            if (mail.Status != OutboxEmailStatus.Failed)
            {
                return Results.Conflict(new
                {
                    message = mail.Status == OutboxEmailStatus.Sent
                        ? "That email was already sent — retrying would deliver it twice."
                        : "That email is still queued and will be attempted on its own."
                });
            }

            Requeue(mail);
            audit.Record(AuditAction.NotificationDispatched,
                $"Requeued a failed {mail.Kind} email to {mail.ToEmail} for another attempt.",
                "OutboxEmail", mail.Id.ToString());
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { message = $"Queued another attempt to {mail.ToEmail}." });
        }

        // POST /api/admin/outbox/retry-failed — the usual case after fixing a mail outage, where
        // failures arrive by the hundred and retrying them one at a time is not a plan.
        private static async Task<IResult> RetryAllFailedAsync(
            AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var failed = await db.OutboxEmails
                .Where(m => m.Status == OutboxEmailStatus.Failed)
                .ToListAsync(ct);

            if (failed.Count == 0)
            {
                return Results.Ok(new { requeued = 0, message = "Nothing has failed permanently." });
            }

            foreach (var mail in failed) Requeue(mail);

            audit.Record(AuditAction.NotificationDispatched,
                $"Requeued {failed.Count} permanently-failed email(s) for another attempt.",
                "OutboxEmail", string.Empty);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                requeued = failed.Count,
                message = $"Queued another attempt for {failed.Count} email(s)."
            });
        }

        /// <summary>
        /// Back to Pending, due now, attempt count reset.
        ///
        /// <para>
        /// The reset matters: leaving <c>Attempts</c> at the cap would let the dispatcher declare
        /// the row failed again on its very first try, which looks from the outside like the retry
        /// button doing nothing. The error text is cleared for the same reason — a stale message
        /// beside a queued row reads as a fresh failure.
        /// </para>
        /// </summary>
        internal static void Requeue(OutboxEmail mail)
        {
            mail.Status = OutboxEmailStatus.Pending;
            mail.Attempts = 0;
            mail.LastError = null;
            mail.NextAttemptAtUtc = DateTime.UtcNow;
        }

        private static string? Iso(DateTime? value) =>
            value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc).ToString("o") : null;
    }
}
