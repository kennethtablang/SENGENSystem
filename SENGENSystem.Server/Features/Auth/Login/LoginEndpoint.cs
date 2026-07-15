using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Auth;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Auth.Login
{
    // Vertical slice: credential login issuing a role-bearing JWT (FR-AUTH-05, 08).
    public record LoginRequest(string Email, string Password);

    public record LoginResponse(string Token, AuthUserDto User);

    public static class LoginEndpoint
    {
        public static IEndpointRouteBuilder MapLogin(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/auth/login", HandleAsync).AllowAnonymous();
            return app;
        }

        private static async Task<IResult> HandleAsync(
            LoginRequest request,
            AppDbContext db,
            IPasswordHasher<User> passwordHasher,
            JwtTokenService tokenService,
            AuditLog audit,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            {
                return Results.BadRequest(new { message = "Email and password are required." });
            }

            var email = request.Email.Trim().ToLowerInvariant();
            var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

            // Same message for unknown email and wrong password, to avoid account enumeration —
            // but the audit trail records the real reason for the School Admin (FR-AUD-01).
            if (user is null || !user.IsActive)
            {
                if (user is null)
                {
                    audit.RecordAnonymous(AuditAction.LoginFailed, "Failed sign-in — no matching account.", email);
                }
                else
                {
                    audit.RecordFor(user, AuditAction.LoginFailed,
                        "Failed sign-in — account is deactivated.", "User", user.Id.ToString());
                }
                await db.SaveChangesAsync(cancellationToken);
                return Results.Json(new { message = "Invalid email or password." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (result == PasswordVerificationResult.Failed)
            {
                audit.RecordFor(user, AuditAction.LoginFailed,
                    "Failed sign-in — incorrect password.", "User", user.Id.ToString());
                await db.SaveChangesAsync(cancellationToken);
                return Results.Json(new { message = "Invalid email or password." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            }

            audit.RecordFor(user, AuditAction.LoginSucceeded, "Signed in.", "User", user.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);

            var token = tokenService.CreateToken(user);
            return Results.Ok(new LoginResponse(token, AuthUserDto.From(user)));
        }
    }
}
