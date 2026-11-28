using SENGENSystem.Server.Common.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Common.Validation;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.UserManagement.ResetUserPassword
{
    // Vertical slice: the School Admin sets a new password for a locked-out user (FR-AUTH-07).
    // Distinct from self-service change — no current password is required.
    public record ResetUserPasswordRequest(string? NewPassword);

    public static class ResetUserPasswordEndpoint
    {
        public static IEndpointRouteBuilder MapResetUserPassword(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/users/{id:guid}/password", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.SuperAdmin)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            Guid id,
            ResetUserPasswordRequest request,
            AppDbContext db,
            IPasswordHasher<User> passwordHasher,
            AuditLog audit,
            HttpContext http,
            CancellationToken cancellationToken)
        {
            if (!PasswordPolicy.IsValid(request.NewPassword))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["newPassword"] = [PasswordPolicy.Message]
                });
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (user is null)
            {
                return Results.NotFound(new { message = "User not found." });
            }

            // Privilege guard (AccountAuthority): a password reset is a takeover of the account,
            // so it is allowed only at or below the caller's own rank.
            if (AccountAuthority.RefuseManaging(http.User, user) is { } refusal)
            {
                return AccountAuthority.Forbidden(refusal);
            }

            user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword!);
            audit.Record(AuditAction.UserAccountUpdated,
                $"Reset the password for {user.FullName} ({user.Email}).",
                "User", user.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new { message = "Password reset." });
        }
    }
}
