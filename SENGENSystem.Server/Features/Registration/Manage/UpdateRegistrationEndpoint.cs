using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Common.Validation;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Documents;

namespace SENGENSystem.Server.Features.Registration.Manage
{
    // Vertical slice: the Registrar corrects registration data, updates the admission-requirements
    // checklist, and confirms/rejects the SIS (FR-SIS-04, FR-DOC-02). Only supplied fields change.
    public record DocumentUpdate(string RequirementCode, string Status);

    public record UpdateRegistrationRequest(
        string? FirstName,
        string? LastName,
        string? MiddleName,
        string? Email,
        string? MobileNumber,
        string? Status,
        IReadOnlyList<DocumentUpdate>? Documents);

    public static class UpdateRegistrationEndpoint
    {
        public static IEndpointRouteBuilder MapUpdateRegistration(this IEndpointRouteBuilder app)
        {
            app.MapPut("/api/registration/{id:guid}", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.Registrar)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            Guid id,
            UpdateRegistrationRequest request,
            AppDbContext db,
            AuditLog audit,
            Notifier notifier,
            CancellationToken cancellationToken)
        {
            var registration = await db.StudentRegistrations
                .Include(r => r.Semester)
                .Include(r => r.Documents)
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

            if (registration is null)
            {
                return Results.NotFound(new { message = "Registration not found." });
            }

            var errors = new Dictionary<string, string[]>();

            if (request.Email is not null && (string.IsNullOrWhiteSpace(request.Email) || !MailAddress.TryCreate(request.Email.Trim(), out _)))
            {
                errors["email"] = ["A valid email address is required."];
            }

            RegistrationStatus? newStatus = null;
            if (request.Status is not null)
            {
                if (Enum.TryParse<RegistrationStatus>(request.Status, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
                {
                    newStatus = parsed;
                }
                else
                {
                    errors["status"] = ["Please choose a valid status."];
                }
            }

            var catalog = await DocumentChecklist.LoadCatalogAsync(db, cancellationToken);

            var docChanges = new List<(string Code, DocumentStatus Status)>();
            if (request.Documents is not null)
            {
                foreach (var d in request.Documents)
                {
                    // The catalog decides which statuses a paper offers — a photocopy, or a
                    // certificate of grades standing in for it, never both.
                    if (!string.IsNullOrWhiteSpace(d.RequirementCode)
                        && Enum.TryParse<DocumentStatus>(d.Status, ignoreCase: true, out var ds) && Enum.IsDefined(ds)
                        && catalog.StatusesFor(d.RequirementCode).Contains(ds))
                    {
                        docChanges.Add((d.RequirementCode, ds));
                    }
                    else
                    {
                        errors["documents"] = ["One or more document entries are invalid."];
                        break;
                    }
                }
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            // ---- Apply corrections ----
            if (!string.IsNullOrWhiteSpace(request.FirstName)) registration.FirstName = NameFormatter.ToProperCase(request.FirstName);
            if (!string.IsNullOrWhiteSpace(request.LastName)) registration.LastName = NameFormatter.ToProperCase(request.LastName);
            if (request.MiddleName is not null) registration.MiddleName = string.IsNullOrWhiteSpace(request.MiddleName) ? string.Empty : NameFormatter.ToProperCase(request.MiddleName);
            if (!string.IsNullOrWhiteSpace(request.Email)) registration.Email = request.Email.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(request.MobileNumber)) registration.MobileNumber = request.MobileNumber.Trim();

            var documentsChanged = false;
            foreach (var (code, status) in docChanges)
            {
                var doc = registration.Documents.FirstOrDefault(d =>
                    string.Equals(d.RequirementCode, code, StringComparison.OrdinalIgnoreCase));
                if (doc is not null && doc.Status != status)
                {
                    doc.Status = status;
                    documentsChanged = true;
                }
            }

            if (documentsChanged)
            {
                var submitted = registration.Documents.Count(d => d.Status != DocumentStatus.NotSubmitted);
                audit.Record(AuditAction.DocumentChecklistUpdated,
                    $"Updated the admission-requirements checklist for {registration.StudentNumber} " +
                    $"({submitted}/{registration.Documents.Count} on file).",
                    "StudentRegistration", registration.Id.ToString());
            }

            if (newStatus is { } status2 && status2 != registration.Status)
            {
                registration.Status = status2;
                if (status2 == RegistrationStatus.Confirmed)
                {
                    audit.Record(AuditAction.RegistrationConfirmed,
                        $"Confirmed the SIS registration of {registration.FullName} ({registration.StudentNumber}).",
                        "StudentRegistration", registration.Id.ToString());
                }

                // Announce the decision on the back-office bells, and to the student's own account
                // when it's a terminal decision (confirmed/rejected). Staged in this transaction.
                if (status2 is RegistrationStatus.Confirmed or RegistrationStatus.Rejected)
                {
                    var verb = status2 == RegistrationStatus.Confirmed ? "confirmed" : "rejected";
                    var staffIds = await NotificationRecipients.StaffUserIdsAsync(db, cancellationToken);
                    notifier.NotifyMany(staffIds, NotificationKind.Registration,
                        $"Registration {verb}",
                        $"{registration.FullName} ({registration.StudentNumber})'s SIS registration was {verb}.",
                        "/registrations");

                    if (registration.UserId is { } studentUserId)
                    {
                        notifier.Notify(studentUserId, NotificationKind.Registration,
                            status2 == RegistrationStatus.Confirmed
                                ? "Your registration is confirmed"
                                : "Your registration was not approved",
                            status2 == RegistrationStatus.Confirmed
                                ? "The Registrar confirmed your SIS registration. You can proceed with the remaining enrollment steps."
                                : "The Registrar could not approve your SIS registration. Please contact the Registrar's office.",
                            "/documents");
                    }
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(StudentRegistrationDto.From(registration, catalog));
        }
    }
}
