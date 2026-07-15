using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Registration.TermActivation
{
    // Vertical slice: the Admission Officer validates (approves) or rejects a returning student's
    // term-activation request. Approval emails the student a confirmation (FR-NOTIF-01).
    public record ValidateTermActivationRequest(bool Approve, string? Remarks);

    public static class ValidateTermActivationEndpoint
    {
        public static IEndpointRouteBuilder MapValidateTermActivation(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/registration/term-activation/{id:guid}/validate", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.AdmissionOfficer)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            Guid id,
            ValidateTermActivationRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            IEmailSender email,
            CancellationToken cancellationToken)
        {
            var activation = await db.TermActivations
                .Include(a => a.StudentRegistration)
                .Include(a => a.Semester)
                .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

            if (activation is null)
            {
                return Results.NotFound(new { message = "Term activation request not found." });
            }

            if (activation.Status != TermActivationStatus.Pending)
            {
                return Results.Conflict(new { message = $"This request has already been {activation.Status.ToString().ToLowerInvariant()}." });
            }

            Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var officerId);

            activation.Status = request.Approve ? TermActivationStatus.Validated : TermActivationStatus.Rejected;
            activation.ValidatedByUserId = officerId == Guid.Empty ? null : officerId;
            activation.ValidatedAtUtc = DateTime.UtcNow;
            activation.Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();

            var registration = activation.StudentRegistration!;
            var semesterName = activation.Semester?.Name ?? "the active term";

            var verb = request.Approve ? "Validated" : "Rejected";
            audit.Record(AuditAction.TermActivationValidated,
                $"{verb} {registration.FullName}'s term activation for {semesterName}" +
                (activation.Remarks is null ? "." : $" — {activation.Remarks}"),
                "TermActivation", activation.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);

            var emailSent = false;
            if (request.Approve)
            {
                var (subject, body) = RegistrationEmails.TermActivationConfirmation(registration, semesterName);
                var result = await email.SendAsync(registration.Email, registration.FullName, subject, body, cancellationToken);
                emailSent = result.Sent;
                if (result.Sent)
                {
                    audit.Record(AuditAction.NotificationDispatched,
                        $"Sent term activation confirmation to {registration.Email}.",
                        "TermActivation", activation.Id.ToString());
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            return Results.Ok(new
            {
                id = activation.Id,
                status = activation.Status.ToString(),
                emailSent
            });
        }
    }
}
