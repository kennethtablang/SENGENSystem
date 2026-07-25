using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Enlistment
{
    /// <summary>
    /// FR-ENL-05: only pre-authorized, registered students may enlist. Resolves the signed-in
    /// student's linked SIS record and states exactly which gate is still closed.
    /// </summary>
    internal sealed record EligibilityResult(StudentRegistration? Registration, IReadOnlyList<string> Blockers)
    {
        public bool IsEligible => Registration is not null && Blockers.Count == 0;
    }

    internal static class EnlistmentEligibility
    {
        public static Guid? CurrentUserId(ClaimsPrincipal principal)
        {
            var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return Guid.TryParse(value, out var id) ? id : null;
        }

        public static async Task<EligibilityResult> ResolveAsync(
            ClaimsPrincipal principal, AppDbContext db, CancellationToken cancellationToken)
        {
            var userId = CurrentUserId(principal);
            if (userId is null)
            {
                return new EligibilityResult(null, ["Your session could not be resolved — please sign in again."]);
            }

            var registration = await db.StudentRegistrations
                .Include(r => r.Documents)
                .FirstOrDefaultAsync(r => r.UserId == userId, cancellationToken);

            if (registration is null)
            {
                return new EligibilityResult(null,
                    ["Your account is not linked to a student record yet. Claim it under Document requirements."]);
            }

            var blockers = new List<string>();
            if (registration.Status != RegistrationStatus.Confirmed)
            {
                blockers.Add("Your SIS registration has not been confirmed by the Registrar yet.");
            }
            // Admission documents may still be arriving — a student can submit them even after
            // enlistment opens — so an incomplete checklist deliberately does NOT block slot
            // selection. The Admission Office still tracks and follows up on the pending papers.
            if (!registration.IsPreAuthorized)
            {
                blockers.Add("The Admission Office has not yet cleared you for online slot selection.");
            }

            return new EligibilityResult(registration, blockers);
        }
    }
}
