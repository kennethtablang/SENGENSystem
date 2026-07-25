using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Registration.TermActivation
{
    // Vertical slice: a returning student self-requests activation for the active term via a public
    // lookup (student number + last name). No SIS re-entry; an Admission Officer validates it later.
    public record RequestTermActivationRequest(string? StudentNumber, string? LastName);

    public record RequestTermActivationResponse(Guid Id, string StudentNumber, string Status, string SemesterName);

    public static class RequestTermActivationEndpoint
    {
        public static IEndpointRouteBuilder MapRequestTermActivation(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/registration/term-activation", HandleAsync).AllowAnonymous();
            return app;
        }

        private static async Task<IResult> HandleAsync(
            RequestTermActivationRequest request,
            AppDbContext db,
            AuditLog audit,
            IEmailSender email,
            Notifier notifier,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.StudentNumber) || string.IsNullOrWhiteSpace(request.LastName))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["studentNumber"] = string.IsNullOrWhiteSpace(request.StudentNumber) ? ["Your student number is required."] : [],
                    ["lastName"] = string.IsNullOrWhiteSpace(request.LastName) ? ["Your last name is required."] : []
                });
            }

            var semester = await db.Semesters.FirstOrDefaultAsync(s => s.IsActive, cancellationToken);
            if (semester is null)
            {
                return Results.BadRequest(new { message = "Term activation is closed: no active semester is set." });
            }

            var studentNumber = request.StudentNumber.Trim();
            var lastName = request.LastName.Trim();

            var registration = await db.StudentRegistrations
                .FirstOrDefaultAsync(r => r.StudentNumber == studentNumber, cancellationToken);

            // Same generic message whether the number is unknown or the name doesn't match — don't
            // confirm which student numbers exist.
            if (registration is null || !string.Equals(registration.LastName, lastName, StringComparison.OrdinalIgnoreCase))
            {
                return Results.NotFound(new { message = "We couldn't find a matching student record. Check your student number and last name." });
            }

            var alreadyActive = await db.TermActivations.AnyAsync(
                a => a.StudentRegistrationId == registration.Id
                     && a.SemesterId == semester.Id
                     && a.Status != TermActivationStatus.Rejected,
                cancellationToken);
            if (alreadyActive)
            {
                return Results.Conflict(new { message = $"You already have a term activation on file for {semester.Name}." });
            }

            var activation = new Domain.TermActivation
            {
                StudentRegistrationId = registration.Id,
                SemesterId = semester.Id,
                Status = TermActivationStatus.Pending
            };
            db.TermActivations.Add(activation);
            audit.RecordAnonymous(AuditAction.TermActivationRequested,
                $"Requested term activation for {semester.Name}.",
                registration.FullName, "TermActivation", activation.Id.ToString());

            // Put the request on every back-office bell (the Admission Office validates it), committed
            // with the activation (FR-NOTIF).
            var staffIds = await NotificationRecipients.StaffUserIdsAsync(db, cancellationToken);
            notifier.NotifyMany(staffIds, NotificationKind.TermActivation,
                "New term-activation request",
                $"{registration.FullName} ({registration.StudentNumber}) requested activation for {semester.Name}.",
                "/term-activations");

            await db.SaveChangesAsync(cancellationToken);

            // Receipt/proof that the request was filed — sent on request, not only on approval
            // (the approval confirmation is a separate email). Best-effort: the request is already
            // committed, so a mail failure must not fail the response.
            var (subject, body) = RegistrationEmails.TermActivationRequested(registration, semester.Name);
            var result = await email.SendAsync(registration.Email, registration.FullName, subject, body, cancellationToken);
            if (result.Sent)
            {
                audit.RecordAnonymous(AuditAction.NotificationDispatched,
                    $"Sent term activation request receipt to {registration.Email}.",
                    registration.FullName, "TermActivation", activation.Id.ToString());
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.Created($"/api/registration/term-activation/{activation.Id}",
                new RequestTermActivationResponse(activation.Id, registration.StudentNumber, activation.Status.ToString(), semester.Name));
        }
    }
}
