using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Common.Validation;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.UserManagement.CreateUser
{
    // Vertical slice: the School Admin creates an account for any of the six roles and sets its
    // initial password (FR-AUTH-07). Names are proper-cased; email must be unique.
    public record CreateUserRequest(
        string? FirstName,
        string? LastName,
        string? Email,
        string? Role,
        string? Password);

    public static class CreateUserEndpoint
    {
        public static IEndpointRouteBuilder MapCreateUser(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/users", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.SchoolAdmin), nameof(UserRole.AcademicHead)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            CreateUserRequest request,
            AppDbContext db,
            IPasswordHasher<User> passwordHasher,
            AuditLog audit,
            HttpContext http,
            CancellationToken cancellationToken)
        {
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

            if (!PasswordPolicy.IsValid(request.Password))
            {
                errors["password"] = [PasswordPolicy.Message];
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            // Privilege guard: only a School Admin may mint another School Admin, and only a Super
            // Admin may mint a Super Admin. An Academic Head has user-management access for
            // staff/students but cannot create a peer or higher administrator.
            if (role == UserRole.SuperAdmin && !http.User.IsInRole(nameof(UserRole.SuperAdmin)))
            {
                return Results.Json(
                    new { message = "Only a Super Admin can create a Super Admin account." },
                    statusCode: StatusCodes.Status403Forbidden);
            }
            if (role == UserRole.SchoolAdmin && !http.User.IsInRole(nameof(UserRole.SchoolAdmin)))
            {
                return Results.Json(
                    new { message = "Only a School Admin can create a School Admin account." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var email = request.Email!.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
            {
                return Results.Conflict(new { message = "An account with this email address already exists." });
            }

            var user = new User
            {
                FirstName = NameFormatter.ToProperCase(request.FirstName!),
                LastName = NameFormatter.ToProperCase(request.LastName!),
                Email = email,
                Role = role,
                IsActive = true
            };
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password!);

            db.Users.Add(user);
            audit.Record(AuditAction.UserAccountCreated,
                $"Created a {user.Role} account for {user.FullName} ({user.Email}).",
                "User", user.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created($"/api/users/{user.Id}", UserDto.From(user));
        }
    }
}
