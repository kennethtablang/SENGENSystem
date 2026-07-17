using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Documents.Reminders
{
    // Vertical slice: automated document-submission reminder emails for incomplete checklists
    // (FR-DOC-05, FR-NOTIF-01). Staff trigger a sweep (or a single student's reminder); each
    // email lists exactly the papers still missing.
    public record SendRemindersRequest(Guid? RegistrationId);

    public record SendRemindersResponse(int Targeted, int EmailsSent);

    public static class SendRemindersEndpoint
    {
        public static IEndpointRouteBuilder MapDocumentReminders(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/documents/reminders", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.AdmissionOfficer), nameof(UserRole.Registrar), nameof(UserRole.SchoolAdmin)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            SendRemindersRequest request,
            AppDbContext db,
            AuditLog audit,
            IEmailSender email,
            CancellationToken cancellationToken)
        {
            var query = db.StudentRegistrations
                .Include(r => r.Documents)
                .Where(r => r.Status != RegistrationStatus.Rejected
                    && r.Documents.Any(d => d.Status == DocumentStatus.NotSubmitted));

            if (request.RegistrationId is { } id)
            {
                query = query.Where(r => r.Id == id);
            }

            var targets = await query.ToListAsync(cancellationToken);
            if (request.RegistrationId is not null && targets.Count == 0)
            {
                return Results.BadRequest(new
                {
                    message = "This enrollee's checklist is already complete (or the record was not found)."
                });
            }

            var emailsSent = 0;
            foreach (var registration in targets)
            {
                var missing = registration.Documents
                    .Where(d => d.Status == DocumentStatus.NotSubmitted)
                    .OrderBy(d => d.DocumentType)
                    .Select(d => d.DocumentType)
                    .ToList();

                var (subject, body) = DocumentEmails.SubmissionReminder(registration, missing);
                var result = await email.SendAsync(
                    registration.Email, registration.FullName, subject, body, cancellationToken);
                if (result.Sent) emailsSent++;
            }

            if (emailsSent > 0)
            {
                audit.Record(AuditAction.NotificationDispatched,
                    $"Sent document submission reminder(s) to {emailsSent} enrollee(s) with incomplete checklists.");
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.Ok(new SendRemindersResponse(targets.Count, emailsSent));
        }
    }
}
