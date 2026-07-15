using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Profile.ChangePassword
{
    // Vertical slice: the signed-in user changes their own password.
    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    public static class ChangePasswordEndpoint
    {
        public static IEndpointRouteBuilder MapChangePassword(this IEndpointRouteBuilder app)
        {
            app.MapPut("/api/profile/password", HandleAsync).RequireAuthorization();
            return app;
        }

        private static async Task<IResult> HandleAsync(
            ChangePasswordRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            IPasswordHasher<User> passwordHasher,
            AuditLog audit,
            CancellationToken cancellationToken)
        {
            var idClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(idClaim, out var userId))
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrEmpty(request.NewPassword)
                || request.NewPassword.Length < 8
                || !request.NewPassword.Any(char.IsLetter)
                || !request.NewPassword.Any(char.IsDigit))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["newPassword"] = ["Password must be at least 8 characters and contain both letters and digits."]
                });
            }

            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, cancellationToken);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var verify = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword ?? string.Empty);
            if (verify == PasswordVerificationResult.Failed)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["currentPassword"] = ["Your current password is incorrect."]
                });
            }

            user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
            audit.Record(AuditAction.PasswordChanged,
                "Changed their own password.", "User", user.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new { message = "Password updated." });
        }
    }
}
