using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.UserManagement.UpdateUser;

namespace SENGENSystem.Server.Features.UserManagement.SetUserActive
{
    // Vertical slice: the School Admin deactivates or reactivates an account (FR-AUTH-07). Login
    // already refuses inactive accounts. Guards prevent locking administration out: an admin
    // can't deactivate their own account or the last active School Admin.
    public record SetUserActiveRequest(bool IsActive);

    public static class SetUserActiveEndpoint
    {
        public static IEndpointRouteBuilder MapSetUserActive(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/users/{id:guid}/active", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.SchoolAdmin), nameof(UserRole.AcademicHead)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            Guid id,
            SetUserActiveRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            CancellationToken cancellationToken)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (user is null)
            {
                return Results.NotFound(new { message = "User not found." });
            }

            // Privilege guard: only a School Admin may activate/deactivate a School Admin account.
            if (user.Role == UserRole.SchoolAdmin && !principal.IsInRole(nameof(UserRole.SchoolAdmin)))
            {
                return Results.Json(
                    new { message = "Only a School Admin can change a School Admin account's status." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            if (!request.IsActive)
            {
                Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var callerId);
                if (callerId == user.Id)
                {
                    return Results.BadRequest(new { message = "You can't deactivate your own account." });
                }

                if (user.Role == UserRole.SchoolAdmin
                    && !await UpdateUserEndpoint.HasOtherActiveAdminAsync(db, user.Id, cancellationToken))
                {
                    return Results.BadRequest(new { message = "You can't deactivate the last active School Admin." });
                }
            }

            if (user.IsActive == request.IsActive)
            {
                return Results.Ok(UserDto.From(user)); // no-op, already in the requested state
            }

            user.IsActive = request.IsActive;

            if (request.IsActive)
            {
                audit.Record(AuditAction.UserAccountUpdated,
                    $"Reactivated the account of {user.FullName} ({user.Email}).",
                    "User", user.Id.ToString());
            }
            else
            {
                audit.Record(AuditAction.UserAccountDeactivated,
                    $"Deactivated the account of {user.FullName} ({user.Email}).",
                    "User", user.Id.ToString());
            }

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(UserDto.From(user));
        }
    }
}
