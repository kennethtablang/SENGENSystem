using SENGENSystem.Server.Common.Auth;
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
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.SuperAdmin)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            Guid id,
            UpdateUserRequest request,
            AppDbContext db,
            AuditLog audit,
            HttpContext http,
            CancellationToken cancellationToken)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (user is null)
            {
                return Results.NotFound(new { message = "User not found." });
            }

            // Privilege guard (AccountAuthority): an account above the caller's rank is not theirs
            // to edit — changing its email is enough to claim it through "forgot password". The
            // role being handed out is checked once it is parsed, below.
            if (AccountAuthority.RefuseManaging(http.User, user) is { } refusal)
            {
                return AccountAuthority.Forbidden(refusal);
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

            // Nobody hands out a role above their own — including to themselves, which is how an
            // Academic Head could previously make themselves Super Admin.
            if (role != user.Role && AccountAuthority.RefuseAssigning(http.User, role) is { } roleRefusal)
            {
                return AccountAuthority.Forbidden(roleRefusal);
            }

            var email = request.Email!.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.Email == email && u.Id != id, cancellationToken))
            {
                return Results.Conflict(new { message = "An account with this email address already exists." });
            }

            // Don't let the last active School Admin — or Super Admin — be demoted out of the role.
            if (role != user.Role && await AccountAuthority.IsLastActiveAdminAsync(db, user, cancellationToken))
            {
                return Results.BadRequest(new { message = $"You can't change the role of the last active {user.Role}." });
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
    }
}
