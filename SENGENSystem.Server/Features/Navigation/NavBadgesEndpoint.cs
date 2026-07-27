using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Navigation
{
    // Vertical slice: the numbered counts the sidebar renders next to a function, so a role sees
    // its outstanding work at a glance (unread notices, pending approvals, registrations awaiting
    // confirmation, term activations awaiting validation). Every count is role-scoped — a role only
    // gets a number for a function it can actually open — so the payload never leaks other queues.
    public record NavBadgesDto(
        int Notifications, int Approvals, int Registrations, int TermActivations, int Survey, int Evaluations);

    public static class NavBadgesEndpoint
    {
        public static IEndpointRouteBuilder MapNavBadges(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/nav/badges", HandleAsync).RequireAuthorization();
            return app;
        }

        private static async Task<IResult> HandleAsync(
            ClaimsPrincipal principal, AppDbContext db, CancellationToken cancellationToken)
        {
            var notifications = 0;
            var survey = 0;
            if (Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                notifications = await db.Notifications.AsNoTracking()
                    .CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);

                // The evaluation survey is only worth pointing at while this user actually owes a
                // response and collection is still open — the sidebar hides the entry otherwise.
                var collectionOpen = await db.SurveyCampaigns.AsNoTracking()
                    .Where(c => c.Id == SurveyCampaign.SingletonId)
                    .Select(c => c.IsOpen)
                    .FirstOrDefaultAsync(cancellationToken);
                if (collectionOpen)
                {
                    survey = await db.SurveyInvitations.AsNoTracking()
                        .CountAsync(i => i.UserId == userId && i.CompletedAtUtc == null, cancellationToken);
                }
            }

            var isRegistrar = principal.IsInRole(nameof(UserRole.Registrar));
            var isAdmission = principal.IsInRole(nameof(UserRole.AdmissionOfficer));
            var isAdmin = principal.IsInRole(nameof(UserRole.SchoolAdmin));

            // Counts follow the active term so a badge matches its (semester-scoped) queue and
            // doesn't carry a past term's outstanding work into the new one.
            var activeSemesterId = await db.GetActiveSemesterIdAsync(cancellationToken);

            var approvals = isRegistrar || isAdmin
                ? await db.SlotRequests.AsNoTracking()
                    .CountAsync(r => r.Status == SlotRequestStatus.Requested
                        && (activeSemesterId == null || r.Section!.SemesterId == activeSemesterId), cancellationToken)
                : 0;

            var registrations = isRegistrar || isAdmin
                ? await db.StudentRegistrations.AsNoTracking()
                    .CountAsync(r => r.Status == RegistrationStatus.Submitted
                        && (activeSemesterId == null || r.SemesterId == activeSemesterId), cancellationToken)
                : 0;

            var termActivations = isAdmission || isAdmin
                ? await db.TermActivations.AsNoTracking()
                    .CountAsync(a => a.Status == TermActivationStatus.Pending
                        && (activeSemesterId == null || a.SemesterId == activeSemesterId), cancellationToken)
                : 0;

            // Transferees still waiting on a completed credit evaluation — they cannot enlist until
            // it is signed off, so this is the queue that holds their whole term up (FR-EVAL).
            var evaluations = isRegistrar || isAdmission || isAdmin
                ? await db.StudentRegistrations.AsNoTracking()
                    .CountAsync(r => r.StudentType == StudentType.Transferee
                        && r.Status != RegistrationStatus.Rejected
                        && (activeSemesterId == null || r.SemesterId == activeSemesterId)
                        && !db.TransfereeEvaluations.Any(e =>
                            e.StudentRegistrationId == r.Id
                            && e.Status == TransfereeEvaluationStatus.Completed), cancellationToken)
                : 0;

            return Results.Ok(new NavBadgesDto(
                notifications, approvals, registrations, termActivations, survey, evaluations));
        }
    }
}
