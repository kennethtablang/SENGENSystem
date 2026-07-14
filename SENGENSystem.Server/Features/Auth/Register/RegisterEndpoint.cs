using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Common.Validation;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Auth.Register
{
    // Vertical slice: student self-service account registration (FR-AUTH-01..04, 06, 09).
    public record RegisterRequest(
        string FirstName,
        string LastName,
        string Email,
        string Password,
        bool AcceptedTerms);

    public record RegisterResponse(AuthUserDto User);

    public static class RegisterEndpoint
    {
        public static IEndpointRouteBuilder MapRegister(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/auth/register", HandleAsync).AllowAnonymous();
            return app;
        }

        private static async Task<IResult> HandleAsync(
            RegisterRequest request,
            AppDbContext db,
            IPasswordHasher<User> passwordHasher,
            CancellationToken cancellationToken)
        {
            var errors = Validate(request);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var email = request.Email.Trim().ToLowerInvariant();
            var emailTaken = await db.Users.AnyAsync(u => u.Email == email, cancellationToken);
            if (emailTaken)
            {
                return Results.Conflict(new { message = "An account with this email address already exists." });
            }

            var user = new User
            {
                FirstName = NameFormatter.ToProperCase(request.FirstName),
                LastName = NameFormatter.ToProperCase(request.LastName),
                Email = email,
                Role = UserRole.Student,
                TermsAcceptedAtUtc = DateTime.UtcNow
            };
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created($"/api/users/{user.Id}", new RegisterResponse(AuthUserDto.From(user)));
        }

        private static Dictionary<string, string[]> Validate(RegisterRequest request)
        {
            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(request.FirstName))
            {
                errors["firstName"] = ["First name is required."];
            }

            if (string.IsNullOrWhiteSpace(request.LastName))
            {
                errors["lastName"] = ["Last name is required."];
            }

            if (string.IsNullOrWhiteSpace(request.Email) || !MailAddress.TryCreate(request.Email.Trim(), out _))
            {
                errors["email"] = ["A valid email address is required."];
            }

            if (string.IsNullOrEmpty(request.Password)
                || request.Password.Length < 8
                || !request.Password.Any(char.IsLetter)
                || !request.Password.Any(char.IsDigit))
            {
                errors["password"] = ["Password must be at least 8 characters and contain both letters and digits."];
            }

            if (!request.AcceptedTerms)
            {
                errors["acceptedTerms"] = ["You must read and accept the terms and conditions to register."];
            }

            return errors;
        }
    }
}
