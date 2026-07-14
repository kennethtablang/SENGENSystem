using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;

namespace SENGENSystem.Server.Features.Auth.Me
{
    // Vertical slice: return the authenticated user's profile (session restore for the client).
    public static class MeEndpoint
    {
        public static IEndpointRouteBuilder MapMe(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/auth/me", HandleAsync).RequireAuthorization();
            return app;
        }

        private static async Task<IResult> HandleAsync(
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken cancellationToken)
        {
            var idClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(idClaim, out var userId))
            {
                return Results.Unauthorized();
            }

            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, cancellationToken);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(AuthUserDto.From(user));
        }
    }
}
