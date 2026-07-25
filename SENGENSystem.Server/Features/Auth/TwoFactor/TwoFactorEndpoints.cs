using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Auth;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Auth.TwoFactor
{
    // Vertical slice: the second leg of a two-factor sign-in (FR-AUTH). The client holds the opaque
    // challenge from the login response; Verify exchanges it plus the emailed code for the JWT,
    // Resend emails a fresh code for the same challenge.
    public record VerifyTwoFactorRequest(string? ChallengeToken, string? Code);

    public record ResendTwoFactorRequest(string? ChallengeToken);

    public static class TwoFactorEndpoints
    {
        public static IEndpointRouteBuilder MapTwoFactor(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/auth/2fa/verify", VerifyAsync).AllowAnonymous();
            app.MapPost("/api/auth/2fa/resend", ResendAsync).AllowAnonymous();
            return app;
        }

        private static async Task<IResult> VerifyAsync(
            VerifyTwoFactorRequest request,
            AppDbContext db,
            JwtTokenService tokenService,
            AuditLog audit,
            CancellationToken cancellationToken)
        {
            var expired = Results.Json(new
            {
                message = "This sign-in has expired. Please sign in again."
            }, statusCode: StatusCodes.Status401Unauthorized);

            if (string.IsNullOrWhiteSpace(request.ChallengeToken) || string.IsNullOrWhiteSpace(request.Code))
            {
                return Results.BadRequest(new { message = "Enter the 6-digit code from your email." });
            }

            var user = await FindByChallengeAsync(db, request.ChallengeToken, cancellationToken);
            if (user is null)
            {
                return expired;
            }

            switch (TwoFactorChallenge.VerifyCode(user, request.Code))
            {
                case TwoFactorChallenge.VerifyResult.Ok:
                    TwoFactorChallenge.Clear(user);
                    audit.RecordFor(user, AuditAction.LoginSucceeded,
                        "Signed in (two-factor verified).", "User", user.Id.ToString());
                    await db.SaveChangesAsync(cancellationToken);
                    var token = tokenService.CreateToken(user);
                    return Results.Ok(new { token, user = AuthUserDto.From(user) });

                case TwoFactorChallenge.VerifyResult.WrongCode:
                    audit.RecordFor(user, AuditAction.TwoFactorFailed,
                        "Entered an incorrect two-factor code.", "User", user.Id.ToString());
                    await db.SaveChangesAsync(cancellationToken);
                    var left = TwoFactorChallenge.MaxAttempts - user.TwoFactorAttempts;
                    return Results.Json(new
                    {
                        message = $"That code isn't right. {left} attempt{(left == 1 ? "" : "s")} left."
                    }, statusCode: StatusCodes.Status401Unauthorized);

                case TwoFactorChallenge.VerifyResult.TooManyAttempts:
                    // Void the challenge so a fresh sign-in (password) is required.
                    TwoFactorChallenge.Clear(user);
                    audit.RecordFor(user, AuditAction.TwoFactorFailed,
                        "Too many incorrect two-factor codes; challenge voided.", "User", user.Id.ToString());
                    await db.SaveChangesAsync(cancellationToken);
                    return Results.Json(new
                    {
                        message = "Too many incorrect codes. Please sign in again."
                    }, statusCode: StatusCodes.Status401Unauthorized);

                default: // Expired — keep the challenge so the user can resend without re-entering the password.
                    return Results.Json(new
                    {
                        message = "That code has expired. Tap “Resend code” for a new one."
                    }, statusCode: StatusCodes.Status401Unauthorized);
            }
        }

        private static async Task<IResult> ResendAsync(
            ResendTwoFactorRequest request,
            AppDbContext db,
            IEmailSender email,
            CancellationToken cancellationToken)
        {
            // Always answers the same way so it can't probe which challenges are live.
            var neutral = Results.Ok(new { message = "If your sign-in is still active, a new code is on its way." });

            if (string.IsNullOrWhiteSpace(request.ChallengeToken))
            {
                return neutral;
            }

            var user = await FindByChallengeAsync(db, request.ChallengeToken, cancellationToken);
            if (user is null)
            {
                return neutral;
            }

            var code = TwoFactorChallenge.RefreshCode(user);
            await db.SaveChangesAsync(cancellationToken);

            var (subject, body) = AccountEmails.TwoFactorCode(user, code, TwoFactorChallenge.CodeMinutes);
            await email.SendAsync(user.Email, user.FullName, subject, body, cancellationToken);
            return neutral;
        }

        private static Task<User?> FindByChallengeAsync(AppDbContext db, string challengeToken, CancellationToken ct)
        {
            var hash = OneTimeToken.Hash(challengeToken.Trim());
            return db.Users.FirstOrDefaultAsync(
                u => u.IsActive && u.TwoFactorEnabled && u.TwoFactorChallengeHash == hash, ct);
        }
    }
}
