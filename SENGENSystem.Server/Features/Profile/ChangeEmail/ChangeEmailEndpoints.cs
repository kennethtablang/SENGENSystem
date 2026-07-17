using System.Net.Mail;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Auth;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Auth;

namespace SENGENSystem.Server.Features.Profile.ChangeEmail
{
    // Vertical slice: secure email change (FR-AUTH). The signed-in user requests the change;
    // it takes effect only after the confirmation link emailed to the NEW address is used —
    // proving mailbox ownership (the hardening flagged in the account-linking security review).
    public record ChangeEmailRequest(string NewEmail, string ConfirmEmail);

    public record ConfirmEmailRequest(string Token);

    public static class ChangeEmailEndpoints
    {
        private const int TokenHours = 24;

        public static IEndpointRouteBuilder MapChangeEmail(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/profile/email/request", RequestAsync).RequireAuthorization();
            // Anonymous on purpose: the user may open the link on a device where they are not signed in.
            app.MapPost("/api/profile/email/confirm", ConfirmAsync);
            return app;
        }

        private static async Task<IResult> RequestAsync(
            ChangeEmailRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            IEmailSender email,
            IOptions<EmailOptions> emailOptions,
            CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return Results.Unauthorized();
            }

            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.NewEmail) || !MailAddress.TryCreate(request.NewEmail.Trim(), out _))
            {
                errors["newEmail"] = ["A valid email address is required."];
            }
            else if (!string.Equals(request.NewEmail.Trim(), request.ConfirmEmail?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                errors["confirmEmail"] = ["The email addresses do not match."];
            }
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, cancellationToken);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var newEmail = request.NewEmail.Trim().ToLowerInvariant();
            if (newEmail == user.Email)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["newEmail"] = ["That is already your current email address."]
                });
            }
            var taken = await db.Users.AnyAsync(u => u.Email == newEmail && u.Id != userId, cancellationToken);
            if (taken)
            {
                return Results.Conflict(new { message = "An account with this email address already exists." });
            }

            var token = OneTimeToken.Generate();
            user.PendingEmail = newEmail;
            user.EmailChangeTokenHash = OneTimeToken.Hash(token);
            user.EmailChangeExpiresUtc = DateTime.UtcNow.AddHours(TokenHours);
            audit.Record(AuditAction.EmailChangeRequested,
                $"Requested to change their email to {newEmail} (pending confirmation).",
                "User", user.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);

            var link = $"{emailOptions.Value.ClientBaseUrl.TrimEnd('/')}/confirm-email?token={Uri.EscapeDataString(token)}";
            var (subject, body) = AccountEmails.ConfirmEmailChange(user, newEmail, link, TokenHours);
            var sent = await email.SendAsync(newEmail, user.FullName, subject, body, cancellationToken);

            return Results.Ok(new
            {
                message = sent.Sent
                    ? $"Confirmation link sent to {newEmail}. Your email changes once you open it."
                    : $"The change is registered, but the confirmation email could not be sent right now. Try again later.",
                pendingEmail = newEmail
            });
        }

        private static async Task<IResult> ConfirmAsync(
            ConfirmEmailRequest request,
            AppDbContext db,
            AuditLog audit,
            Notifier notifier,
            CancellationToken cancellationToken)
        {
            var invalid = Results.BadRequest(new
            {
                message = "This confirmation link is invalid or has expired. Request the change again from Profile settings."
            });

            if (string.IsNullOrWhiteSpace(request.Token))
            {
                return invalid;
            }

            var tokenHash = OneTimeToken.Hash(request.Token);
            var user = await db.Users.FirstOrDefaultAsync(
                u => u.EmailChangeTokenHash == tokenHash
                    && u.EmailChangeExpiresUtc > DateTime.UtcNow
                    && u.PendingEmail != null
                    && u.IsActive,
                cancellationToken);
            if (user is null)
            {
                return invalid;
            }

            var newEmail = user.PendingEmail!;
            // The address may have been claimed by someone else while the link sat in the inbox.
            var taken = await db.Users.AnyAsync(u => u.Email == newEmail && u.Id != user.Id, cancellationToken);
            if (taken)
            {
                user.PendingEmail = null;
                user.EmailChangeTokenHash = null;
                user.EmailChangeExpiresUtc = null;
                await db.SaveChangesAsync(cancellationToken);
                return Results.Conflict(new { message = "That email address has since been taken by another account." });
            }

            var oldEmail = user.Email;
            user.Email = newEmail;
            user.PendingEmail = null;
            user.EmailChangeTokenHash = null;
            user.EmailChangeExpiresUtc = null;
            audit.RecordFor(user, AuditAction.EmailChanged,
                $"Confirmed email change from {oldEmail} to {newEmail}.", "User", user.Id.ToString());
            notifier.Notify(user.Id, NotificationKind.Account,
                "Your sign-in email was changed",
                $"Your account email is now {newEmail}. Use it the next time you sign in.",
                "/profile");
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new
            {
                message = "Email confirmed. Sign in with your new address from now on.",
                email = newEmail
            });
        }
    }
}
