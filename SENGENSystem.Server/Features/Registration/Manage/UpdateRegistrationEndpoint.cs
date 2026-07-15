using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Common.Validation;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Registration.Manage
{
    // Vertical slice: the Registrar corrects registration data, updates the admission-requirements
    // checklist, and confirms/rejects the SIS (FR-SIS-04, FR-DOC-02). Only supplied fields change.
    public record DocumentUpdate(string DocumentType, string Status);

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

            var docChanges = new List<(DocumentType Type, DocumentStatus Status)>();
            if (request.Documents is not null)
            {
                foreach (var d in request.Documents)
                {
                    if (Enum.TryParse<DocumentType>(d.DocumentType, ignoreCase: true, out var dt) && Enum.IsDefined(dt)
                        && Enum.TryParse<DocumentStatus>(d.Status, ignoreCase: true, out var ds) && Enum.IsDefined(ds))
                    {
                        docChanges.Add((dt, ds));
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
            foreach (var (type, status) in docChanges)
            {
                var doc = registration.Documents.FirstOrDefault(d => d.DocumentType == type);
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
            }

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(StudentRegistrationDto.From(registration));
        }
    }
}
