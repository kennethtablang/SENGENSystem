using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            {
                return Results.BadRequest(new { message = "Email and password are required." });
            }

            var email = request.Email.Trim().ToLowerInvariant();
            var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

            // Same message for unknown email and wrong password, to avoid account enumeration.
            if (user is null || !user.IsActive)
            {
                return Results.Json(new { message = "Invalid email or password." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (result == PasswordVerificationResult.Failed)
            {
                return Results.Json(new { message = "Invalid email or password." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
                await db.SaveChangesAsync(cancellationToken);
            }

            var token = tokenService.CreateToken(user);
            return Results.Ok(new LoginResponse(token, AuthUserDto.From(user)));
        }
    }
}
