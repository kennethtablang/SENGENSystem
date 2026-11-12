using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;

namespace SENGENSystem.Server.Common.Auth
{
    /// <summary>
    /// <see cref="Domain.User.MustChangePassword"/>, enforced by the server rather than only by the
    /// screen. The flag marks a password someone else knows — a provisioned student's emailed
    /// temporary password, or an admin seeded with the default that sits in source control — and
    /// the client's first-login screen was the only thing honouring it: the same token could call
    /// any endpoint directly and never change the password at all.
    /// <para>
    /// While the flag is set, an authenticated <c>/api</c> request is refused with 403 unless it is
    /// one of the few the first-login screen needs: the auth endpoints (who am I, sign out, 2FA) and
    /// the password change itself. The flag is read from the account row, not the token, because
    /// the client keeps its token across the change — a claim baked in at sign-in would still say
    /// "must change" after the password had been changed.
    /// </para>
    /// </summary>
    public static class PasswordChangeGate
    {
        public static IApplicationBuilder UsePasswordChangeGate(this IApplicationBuilder app) =>
            app.Use(async (context, next) =>
            {
                var path = context.Request.Path;
                if (context.User.Identity?.IsAuthenticated != true
                    || !path.StartsWithSegments("/api")
                    || path.StartsWithSegments("/api/auth")
                    || (path.StartsWithSegments("/api/profile/password") && HttpMethods.IsPut(context.Request.Method)))
                {
                    await next();
                    return;
                }

                var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? context.User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);
                if (Guid.TryParse(id, out var userId))
                {
                    var db = context.RequestServices.GetRequiredService<AppDbContext>();
                    var mustChange = await db.Users.AsNoTracking()
                        .Where(u => u.Id == userId)
                        .Select(u => u.MustChangePassword)
                        .FirstOrDefaultAsync(context.RequestAborted);
                    if (mustChange)
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            message = "Set a new password before continuing — the one you signed in with was issued to you."
                        }, context.RequestAborted);
                        return;
                    }
                }

                await next();
            });
    }
}
