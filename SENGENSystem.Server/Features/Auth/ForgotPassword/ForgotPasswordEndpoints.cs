using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Auth;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Common.Validation;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Auth.ForgotPassword
{
    // Vertical slice: self-service password recovery (FR-AUTH). The request leg always
    // answers the same way so it cannot be used to probe which emails have accounts;
    // the emailed one-time token (hash stored, 60-minute expiry) authorizes the reset.
    public record ForgotPasswordRequest(string Email);

    public record ResetPasswordRequest(string Email, string Token, string NewPassword);

    public static class ForgotPasswordEndpoints
    {
        private const int TokenMinutes = 60;

        public static IEndpointRouteBuilder MapForgotPassword(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/auth/forgot-password", RequestAsync);
            app.MapPost("/api/auth/reset-password", ResetAsync);
            return app;
        }

        private static async Task<IResult> RequestAsync(
            ForgotPasswordRequest request,
            AppDbContext db,
            AuditLog audit,
            IEmailSender email,
            IOptions<EmailOptions> emailOptions,
            CancellationToken cancellationToken)
        {
            var neutral = Results.Ok(new
            {
                message = "If that email has an account, a reset link is on its way. Check your inbox."
            });

            if (string.IsNullOrWhiteSpace(request.Email) || !MailAddress.TryCreate(request.Email.Trim(), out _))
            {
                return neutral;
            }

            var address = request.Email.Trim().ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == address && u.IsActive, cancellationToken);
            if (user is null)
            {
                return neutral;
            }

            var token = OneTimeToken.Generate();
            user.PasswordResetTokenHash = OneTimeToken.Hash(token);
            user.PasswordResetExpiresUtc = DateTime.UtcNow.AddMinutes(TokenMinutes);
            audit.RecordFor(user, AuditAction.PasswordResetRequested,
                "Requested a password reset link.", "User", user.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);

            var link = $"{emailOptions.Value.ClientBaseUrl.TrimEnd('/')}/reset-password" +
                       $"?email={Uri.EscapeDataString(user.Email)}&token={Uri.EscapeDataString(token)}";
            var (subject, body) = AccountEmails.PasswordReset(user, link, TokenMinutes);
            await email.SendAsync(user.Email, user.FullName, subject, body, cancellationToken);

            return neutral;
        }

        private static async Task<IResult> ResetAsync(
            ResetPasswordRequest request,
            AppDbContext db,
            AuditLog audit,
            Notifier notifier,
            IPasswordHasher<User> hasher,
            CancellationToken cancellationToken)
        {
            if (!PasswordPolicy.IsValid(request.NewPassword))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["newPassword"] = [PasswordPolicy.Message]
                });
            }

            var invalid = Results.BadRequest(new
            {
                message = "This reset link is invalid or has expired. Request a new one from the sign-in page."
            });

            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Token))
            {
                return invalid;
            }

            var address = request.Email.Trim().ToLowerInvariant();
            var tokenHash = OneTimeToken.Hash(request.Token);
            var user = await db.Users.FirstOrDefaultAsync(
                u => u.Email == address && u.IsActive
                    && u.PasswordResetTokenHash == tokenHash
                    && u.PasswordResetExpiresUtc > DateTime.UtcNow,
                cancellationToken);
            if (user is null)
            {
                return invalid;
            }

            user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
            user.PasswordResetTokenHash = null;
            user.PasswordResetExpiresUtc = null;
            audit.RecordFor(user, AuditAction.PasswordResetCompleted,
                "Reset their password via an emailed link.", "User", user.Id.ToString());
            notifier.Notify(user.Id, NotificationKind.Account,
                "Your password was changed",
                "Your password was just reset through the forgot-password link. If this wasn't you, contact the School Admin immediately.",
                "/profile");
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new { message = "Password updated. You can now sign in with your new password." });
        }
    }
}
