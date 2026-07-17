using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

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

            var requests = await db.SlotRequests.AsNoTracking()
                .Where(r => r.StudentRegistrationId == eligibility.Registration.Id)
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

        private static async Task<IResult> CancelAsync(
            Guid requestId,
            System.Security.Claims.ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
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
            if (request.Status != SlotRequestStatus.Requested)
            {
                return Results.Conflict(new
                {
                    message = $"Only pending requests can be cancelled — this one is {request.Status}."
                });
            }

            request.Status = SlotRequestStatus.Cancelled;
            request.DecidedAtUtc = DateTime.UtcNow;
            audit.Record(AuditAction.SlotRequested,
                $"{request.StudentRegistration!.StudentNumber} cancelled their seat request for " +
                $"{request.Section?.Subject?.Code} ({request.Section?.SectionCode}).",
                "SlotRequest", request.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new { requestId = request.Id, status = request.Status.ToString() });
        }
    }
}
