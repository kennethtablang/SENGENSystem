using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.EnrollmentCycle;

namespace SENGENSystem.Server.Features.Enlistment.MyEnlistment
{
    // Vertical slice: the student's own enlistment state — every request with its status,
    // plus cancellation of still-pending requests (FR-ENL-04).
    public record MyRequestDto(
        Guid RequestId,
        Guid SectionId,
        string SubjectCode,
        string SubjectTitle,
        int Units,
        string SectionCode,
        string Status,
        string RequestedAtUtc,
        string? DecidedAtUtc,
        string? RejectionReason)
    {
        public static MyRequestDto From(SlotRequest r) =>
            new(
                r.Id,
                r.SectionId,
                r.Section?.Subject?.Code ?? string.Empty,
                r.Section?.Subject?.Title ?? string.Empty,
                r.Section?.Subject?.Units ?? 0,
                r.Section?.SectionCode ?? string.Empty,
                r.Status.ToString(),
                Utc(r.RequestedAtUtc)!,
                Utc(r.DecidedAtUtc),
                r.RejectionReason);

        private static string? Utc(DateTime? value) =>
            value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc).ToString("o") : null;
    }

    public static class MyEnlistmentEndpoint
    {
        public static IEndpointRouteBuilder MapMyEnlistment(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/enlistment/mine", GetAsync)
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.Student)));
            app.MapDelete("/api/enlistment/requests/{requestId:guid}", CancelAsync)
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.Student)));
            return app;
        }

        private static async Task<IResult> GetAsync(
            System.Security.Claims.ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken cancellationToken)
        {
            var eligibility = await EnlistmentEligibility.ResolveAsync(principal, db, cancellationToken);
            if (eligibility.Registration is null)
            {
                return Results.Ok(new
                {
                    eligibility = new { eligible = false, blockers = eligibility.Blockers },
                    approvedUnits = 0,
                    count = 0,
                    requests = Array.Empty<MyRequestDto>()
                });
            }

            // This term's enlistment, not the student's whole history. Unscoped, a returning student
            // opened the page after a rollover to last term's approved subjects presented as current
            // — and `approvedUnits` summed every term they had ever enrolled in, which is also the
            // number the per-student unit ceiling is judged against elsewhere.
            var activeSemesterId = await db.GetActiveSemesterIdAsync(cancellationToken);
            var requests = await db.SlotRequests.AsNoTracking()
                .Where(r => r.StudentRegistrationId == eligibility.Registration.Id
                    && (activeSemesterId == null || r.Section!.SemesterId == activeSemesterId))
                .Include(r => r.Section).ThenInclude(s => s!.Subject)
                .OrderByDescending(r => r.RequestedAtUtc)
                .ToListAsync(cancellationToken);

            var rows = requests.Select(MyRequestDto.From).ToList();
            return Results.Ok(new
            {
                eligibility = new
                {
                    eligible = eligibility.IsEligible,
                    studentNumber = eligibility.Registration.StudentNumber,
                    blockers = eligibility.Blockers
                },
                approvedUnits = rows.Where(r => r.Status == nameof(SlotRequestStatus.Approved)).Sum(r => r.Units),
                count = rows.Count,
                requests = rows
            });
        }

        /// <summary>
        /// DELETE /api/enlistment/requests/{id} — the student withdraws from a section, whether the
        /// seat was granted yet or not (FR-ENL-04).
        ///
        /// <para>Two different things share this one route because they are one thing to the
        /// student ("I don't want this class"). A <b>pending</b> request is simply cancelled — it
        /// never held a seat. An <b>approved</b> one is <i>dropped</i>, which returns the seat
        /// through <see cref="SeatRelease"/>; that path did not exist before, so a student approved
        /// into the wrong section had no way out but a visit to the Registrar, and the seat stayed
        /// spent for the term either way.</para>
        ///
        /// <para>Dropping is bounded by the enlistment window: once the term leaves the enlistment
        /// stage the roster is the Registrar's to change, not the student's — staff keep their own
        /// drop on the approvals queue. Cancelling a pending request stays available regardless,
        /// since withdrawing a request nobody has acted on costs the institution nothing.</para>
        /// </summary>
        private static async Task<IResult> CancelAsync(
            Guid requestId,
            System.Security.Claims.ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            Notifier notifier,
            Features.Reports.Live.ReportsBroadcaster broadcaster,
            CancellationToken cancellationToken)
        {
            var userId = EnlistmentEligibility.CurrentUserId(principal);
            var request = await db.SlotRequests
                .Include(r => r.Section).ThenInclude(s => s!.Subject)
                .Include(r => r.StudentRegistration)
                .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

            if (request is null || request.StudentRegistration?.UserId != userId)
            {
                return Results.NotFound(new { message = "Request not found." });
            }

            if (request.Status == SlotRequestStatus.Approved)
            {
                var window = await EnrollmentCyclePolicy.CheckEnlistmentAsync(db, cancellationToken);
                if (!window.Open)
                {
                    return Results.Json(new
                    {
                        message = "You can no longer drop this class yourself — " + window.Reason
                                  + " Ask the Registrar to release the seat."
                    }, statusCode: StatusCodes.Status403Forbidden);
                }

                var outcome = await SeatRelease.ReleaseAsync(
                    request, userId, reason: null, byStudent: true,
                    db, audit, notifier, cancellationToken);
                if (!outcome.Dropped)
                {
                    return Results.Conflict(new { message = outcome.Reason });
                }

                broadcaster.Announce("enlistment");
                return Results.Ok(new { requestId = request.Id, status = request.Status.ToString() });
            }

            if (request.Status != SlotRequestStatus.Requested)
            {
                return Results.Conflict(new
                {
                    message = $"This request is already {request.Status} — there is nothing to withdraw."
                });
            }

            request.Status = SlotRequestStatus.Cancelled;
            request.DecidedAtUtc = DateTime.UtcNow;
            // Its own action. Recorded as SlotRequested, the trail could not tell a seat request
            // from a student taking one back — the two read identically on the audit page.
            audit.Record(AuditAction.SlotCancelled,
                $"{request.StudentRegistration!.StudentNumber} cancelled their seat request for " +
                $"{request.Section?.Subject?.Code} ({request.Section?.SectionCode}).",
                "SlotRequest", request.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);

            broadcaster.Announce("enlistment");
            return Results.Ok(new { requestId = request.Id, status = request.Status.ToString() });
        }
    }
}
