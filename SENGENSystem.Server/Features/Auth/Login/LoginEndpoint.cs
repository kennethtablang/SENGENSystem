using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Auth;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Auth.Login
{
    // Vertical slice: credential login issuing a role-bearing JWT (FR-AUTH-05, 08). When the account
    // has opted into two-factor auth, the password step instead hands back a one-time challenge and
    // emails a 6-digit code; TwoFactorEndpoints.Verify exchanges the code for the JWT (FR-AUTH).
    public record LoginRequest(string Email, string Password);

    public record LoginResponse(string Token, AuthUserDto User);

    public static class LoginEndpoint
    {
        public static IEndpointRouteBuilder MapLogin(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/auth/login", HandleAsync)
                .AllowAnonymous()
                .RequireRateLimiting(SENGENSystem.Server.Program.LoginRateLimitPolicy);
            return app;
        }

        private static async Task<IResult> HandleAsync(
            LoginRequest request,
            AppDbContext db,
            IPasswordHasher<User> passwordHasher,
            JwtTokenService tokenService,
            AuditLog audit,
            IEmailSender emailSender,
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

            // Checked before the password is even verified: while a lockout is in force there is no
            // answer a guesser can extract, correct or not. See LoginThrottle for why this exists
            // alongside the IP rate limiter rather than instead of it.
            var now = DateTime.UtcNow;
            if (user.IsLockedOut(now))
            {
                audit.RecordFor(user, AuditAction.LoginFailed,
                    "Sign-in refused — account is temporarily locked after repeated failures.",
                    "User", user.Id.ToString());
                await db.SaveChangesAsync(cancellationToken);
                return Results.Json(
                    new { message = LoginThrottle.Message(user.LockedOutUntilUtc!.Value, now) },
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (result == PasswordVerificationResult.Failed)
            {
                var justLocked = LoginThrottle.RegisterFailure(user, now);
                audit.RecordFor(user, AuditAction.LoginFailed,
                    justLocked
                        ? $"Failed sign-in — incorrect password. Account locked for "
                          + $"{LoginThrottle.LockoutDuration.TotalMinutes:N0} minutes after "
                          + $"{LoginThrottle.MaxAttempts} consecutive failures."
                        : "Failed sign-in — incorrect password.",
                    "User", user.Id.ToString());
                await db.SaveChangesAsync(cancellationToken);

                return justLocked
                    ? Results.Json(
                        new { message = LoginThrottle.Message(user.LockedOutUntilUtc!.Value, now) },
                        statusCode: StatusCodes.Status429TooManyRequests)
                    : Results.Json(new { message = "Invalid email or password." },
                        statusCode: StatusCodes.Status401Unauthorized);
            }

            // The password was right, so the failure streak ends here — a typo before a correct
            // entry must not carry over and lock the account on some later day.
            LoginThrottle.RegisterSuccess(user);

            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            }

            // Password is correct. If the account uses two-factor auth, do NOT issue the JWT yet —
            // email a one-time code and hand back an opaque challenge the verify step exchanges.
            if (user.TwoFactorEnabled)
            {
                var challenge = TwoFactorChallenge.Issue(user);
                audit.RecordFor(user, AuditAction.TwoFactorChallengeIssued,
                    "Password accepted; a two-factor sign-in code was emailed.", "User", user.Id.ToString());
                await db.SaveChangesAsync(cancellationToken);

                var (subject, body) = AccountEmails.TwoFactorCode(user, challenge.Code, TwoFactorChallenge.CodeMinutes);
                await emailSender.SendAsync(user.Email, user.FullName, subject, body, cancellationToken);

                return Results.Ok(new { twoFactorRequired = true, challengeToken = challenge.Token });
            }

            audit.RecordFor(user, AuditAction.LoginSucceeded, "Signed in.", "User", user.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);

            var token = tokenService.CreateToken(user);
            return Results.Ok(new LoginResponse(token, AuthUserDto.From(user)));
        }
    }
}
