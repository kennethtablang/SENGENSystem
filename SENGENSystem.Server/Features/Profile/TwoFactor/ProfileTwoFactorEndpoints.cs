using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Auth;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Auth;

namespace SENGENSystem.Server.Features.Profile.TwoFactor
{
    // Vertical slice: a signed-in user turns two-factor auth on or off for their own account (FR-AUTH,
    // opt-in). Enabling is a two-step email-code confirmation (proves the mailbox receives codes);
    // disabling re-checks the account password so a merely unlocked session can't switch it off.
    public record EnableTwoFactorRequest(string? Code);

    public record DisableTwoFactorRequest(string? Password);

    public static class ProfileTwoFactorEndpoints
    {
        public static IEndpointRouteBuilder MapProfileTwoFactor(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/profile/2fa").RequireAuthorization();
            group.MapPost("/start", StartAsync);
            group.MapPost("/enable", EnableAsync);
            group.MapPost("/disable", DisableAsync);
            return app;
        }

        private static async Task<IResult> StartAsync(
            ClaimsPrincipal principal, AppDbContext db, IEmailSender email, CancellationToken ct)
        {
            if (await CurrentUserAsync(principal, db, ct) is not { } user)
            {
                return Results.Unauthorized();
            }
            if (user.TwoFactorEnabled)
            {
                return Results.Conflict(new { message = "Two-factor authentication is already on for your account." });
            }

            var code = TwoFactorChallenge.IssueCodeOnly(user);
            await db.SaveChangesAsync(ct);

            var (subject, body) = AccountEmails.TwoFactorCode(user, code, TwoFactorChallenge.CodeMinutes);
            await email.SendAsync(user.Email, user.FullName, subject, body, ct);
            return Results.Ok(new { message = $"We emailed a code to {user.Email}. Enter it to turn on two-factor sign-in." });
        }

        private static async Task<IResult> EnableAsync(
            EnableTwoFactorRequest request, ClaimsPrincipal principal, AppDbContext db,
            AuditLog audit, Notifier notifier, CancellationToken ct)
        {
            if (await CurrentUserAsync(principal, db, ct) is not { } user)
            {
                return Results.Unauthorized();
            }
            if (user.TwoFactorEnabled)
            {
                return Results.Ok(new { twoFactorEnabled = true });
            }

            switch (TwoFactorChallenge.VerifyCode(user, request.Code))
            {
                case TwoFactorChallenge.VerifyResult.Ok:
                    user.TwoFactorEnabled = true;
                    TwoFactorChallenge.Clear(user);
                    audit.Record(AuditAction.TwoFactorEnabled,
                        "Turned on two-factor authentication.", "User", user.Id.ToString());
                    notifier.Notify(user.Id, NotificationKind.Account,
                        "Two-factor authentication is on",
                        "From now on, signing in asks for a one-time code emailed to you. If this wasn't you, change your password.",
                        "/profile");
                    await db.SaveChangesAsync(ct);
                    return Results.Ok(new { twoFactorEnabled = true });

                case TwoFactorChallenge.VerifyResult.WrongCode:
                    await db.SaveChangesAsync(ct);
                    var left = TwoFactorChallenge.MaxAttempts - user.TwoFactorAttempts;
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["code"] = [$"That code isn't right. {left} attempt{(left == 1 ? "" : "s")} left."]
                    });

                case TwoFactorChallenge.VerifyResult.TooManyAttempts:
                    TwoFactorChallenge.Clear(user);
                    await db.SaveChangesAsync(ct);
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["code"] = ["Too many tries. Start again to get a new code."]
                    });

                default:
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["code"] = ["That code has expired. Start again to get a new one."]
                    });
            }
        }

        private static async Task<IResult> DisableAsync(
            DisableTwoFactorRequest request, ClaimsPrincipal principal, AppDbContext db,
            IPasswordHasher<User> passwordHasher, AuditLog audit, Notifier notifier, CancellationToken ct)
        {
            if (await CurrentUserAsync(principal, db, ct) is not { } user)
            {
                return Results.Unauthorized();
            }
            if (!user.TwoFactorEnabled)
            {
                return Results.Ok(new { twoFactorEnabled = false });
            }

            var verify = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? string.Empty);
            if (verify == PasswordVerificationResult.Failed)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["password"] = ["Your password is incorrect."]
                });
            }

            user.TwoFactorEnabled = false;
            TwoFactorChallenge.Clear(user);
            audit.Record(AuditAction.TwoFactorDisabled,
                "Turned off two-factor authentication.", "User", user.Id.ToString());
            notifier.Notify(user.Id, NotificationKind.Account,
                "Two-factor authentication is off",
                "Your account no longer asks for an email code at sign-in. If this wasn't you, change your password and turn it back on.",
                "/profile");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { twoFactorEnabled = false });
        }

        private static async Task<User?> CurrentUserAsync(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
        {
            return Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
                ? await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.IsActive, ct)
                : null;
        }
    }
}
