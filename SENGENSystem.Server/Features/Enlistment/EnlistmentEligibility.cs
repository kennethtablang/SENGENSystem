using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Documents;

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
            if (!DocumentChecklist.IsComplete(registration.Documents))
            {
                blockers.Add("Your admission document checklist is not complete yet.");
            }
            if (!registration.IsPreAuthorized)
            {
                blockers.Add("The Admission Office has not yet cleared you for online slot selection.");
            }

            return new EligibilityResult(registration, blockers);
        }
    }
}
