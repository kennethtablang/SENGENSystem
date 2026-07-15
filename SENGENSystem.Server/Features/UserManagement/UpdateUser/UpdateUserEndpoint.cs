using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Common.Validation;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.UserManagement.UpdateUser
{
    // Vertical slice: the School Admin corrects an account's name/email and reassigns its role
    // (FR-AUTH-07). Guards against demoting the last active School Admin, which would lock
    // administration out of the system.
    public record UpdateUserRequest(string? FirstName, string? LastName, string? Email, string? Role);

    public static class UpdateUserEndpoint
    {
        public static IEndpointRouteBuilder MapUpdateUser(this IEndpointRouteBuilder app)
        {
            app.MapPut("/api/users/{id:guid}", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.SchoolAdmin)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            Guid id,
            UpdateUserRequest request,
            AppDbContext db,
            AuditLog audit,
            CancellationToken cancellationToken)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (user is null)
            {
                return Results.NotFound(new { message = "User not found." });
            }

            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(request.FirstName)) errors["firstName"] = ["First name is required."];
            if (string.IsNullOrWhiteSpace(request.LastName)) errors["lastName"] = ["Last name is required."];

            if (string.IsNullOrWhiteSpace(request.Email) || !MailAddress.TryCreate(request.Email.Trim(), out _))
            {
                errors["email"] = ["A valid email address is required."];
            }

            if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role) || !Enum.IsDefined(role))
            {
                errors["role"] = ["Please choose a valid role."];
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var email = request.Email!.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.Email == email && u.Id != id, cancellationToken))
            {
                return Results.Conflict(new { message = "An account with this email address already exists." });
            }

            // Don't let the last active School Admin be demoted out of the role.
            var demotingAdmin = user.Role == UserRole.SchoolAdmin && role != UserRole.SchoolAdmin;
            if (demotingAdmin && !await HasOtherActiveAdminAsync(db, user.Id, cancellationToken))
            {
                return Results.BadRequest(new { message = "You can't change the role of the last active School Admin." });
            }

            user.FirstName = NameFormatter.ToProperCase(request.FirstName!);
            user.LastName = NameFormatter.ToProperCase(request.LastName!);
            user.Email = email;
            user.Role = role;

            audit.Record(AuditAction.UserAccountUpdated,
                $"Updated the account of {user.FullName} ({user.Email}); role is {user.Role}.",
                "User", user.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(UserDto.From(user));
        }

        internal static Task<bool> HasOtherActiveAdminAsync(AppDbContext db, Guid excludingUserId, CancellationToken ct) =>
            db.Users.AnyAsync(u => u.Role == UserRole.SchoolAdmin && u.IsActive && u.Id != excludingUserId, ct);
    }
}
