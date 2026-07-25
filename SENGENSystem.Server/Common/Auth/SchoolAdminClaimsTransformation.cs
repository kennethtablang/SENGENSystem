using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Common.Auth
{
    /// <summary>
    /// The two administrator roles oversee the whole institution and must reach every function
    /// (FR-AUTH-08) without appending their names to every <c>RequireRole(...)</c>. This
    /// transformation grants the extra role claims:
    /// <list type="bullet">
    /// <item><see cref="UserRole.SuperAdmin"/> — every role, including the SuperAdmin-only surfaces
    /// (user management, the ISO 25010 rating survey). It is the top of the hierarchy.</item>
    /// <item><see cref="UserRole.SchoolAdmin"/> — every role <b>except</b> SuperAdmin, so it keeps
    /// its institution-wide reach but can't touch super-admin-exclusive functions.</item>
    /// </list>
    /// A single point of truth: any current or future <c>RequireRole</c> passes for the right admin,
    /// and nothing else is affected.
    /// </summary>
    public sealed class SchoolAdminClaimsTransformation : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
            {
                return Task.FromResult(principal);
            }

            var isSuperAdmin = principal.IsInRole(nameof(UserRole.SuperAdmin));
            var isSchoolAdmin = principal.IsInRole(nameof(UserRole.SchoolAdmin));
            if (!isSuperAdmin && !isSchoolAdmin)
            {
                return Task.FromResult(principal);
            }

            // A School Admin is elevated to everything except SuperAdmin; a Super Admin gets all.
            foreach (var role in Enum.GetNames<UserRole>())
            {
                if (!isSuperAdmin && role == nameof(UserRole.SuperAdmin))
                {
                    continue;
                }
                // Idempotent: the pipeline can invoke this more than once per request.
                if (!principal.IsInRole(role))
                {
                    identity.AddClaim(new Claim(identity.RoleClaimType, role));
                }
            }

            return Task.FromResult(principal);
        }
    }
}
