using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Documents;

namespace SENGENSystem.Server.Features.PreEnrollment.PreAuthorize
{
    // Vertical slice: the Admission Officer pre-authorizes incoming and returning students for
    // online subject slot selection (FR-PRE-02/04). The gate is a Registrar-confirmed SIS plus the
    // papers the catalog flags as required for authorization — for a new enrollee the report card
    // and good moral, for a transferee the transcript and honorable dismissal. The rest of the
    // checklist may still be arriving and is followed up on rather than blocked on.
    public record PreAuthorizationRowDto(
        Guid RegistrationId,
        string StudentNumber,
        string FullName,
        string Program,
        string StudentType,
        string RegistrationStatus,
        string? SemesterName,
        bool DocumentsComplete,
        int SubmittedCount,
        int TotalCount,
        IReadOnlyList<string> MissingAuthorizationRequirements,
        bool HasLinkedAccount,
        bool IsPreAuthorized,
        string? PreAuthorizedAtUtc)
    {
        public static PreAuthorizationRowDto From(StudentRegistration r, RequirementCatalog catalog)
        {
            var documents = DocumentChecklist.Applicable(r, catalog);
            return new(
                r.Id,
                r.StudentNumber,
                r.FullName,
                r.Program.ToString(),
                r.StudentType.ToString(),
                r.Status.ToString(),
                r.Semester?.Name,
                DocumentChecklist.IsComplete(documents),
                DocumentChecklist.SubmittedCount(documents),
                documents.Count,
                DocumentChecklist.MissingAuthorizationRequirements(documents, catalog),
                r.UserId is not null,
                r.IsPreAuthorized,
                r.PreAuthorizedAtUtc is { } at
                    ? DateTime.SpecifyKind(at, DateTimeKind.Utc).ToString("o")
                    : null);
        }
    }

    public static class PreAuthorizeEndpoints
    {
        public static IEndpointRouteBuilder MapPreAuthorization(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/pre-authorization")
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.AdmissionOfficer), nameof(UserRole.Registrar), nameof(UserRole.SchoolAdmin)));

            group.MapGet("", ListAsync);
            group.MapPost("{registrationId:guid}", GrantAsync);
            group.MapDelete("{registrationId:guid}", RevokeAsync);
            return app;
        }

        private static async Task<IResult> ListAsync(
            string? search,
            AppDbContext db,
            CancellationToken cancellationToken)
        {
            var query = db.StudentRegistrations
                .AsNoTracking()
                .Include(r => r.Semester)
                .Include(r => r.Documents)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(r =>
                    r.StudentNumber.Contains(term)
                    || r.LastName.Contains(term)
                    || r.FirstName.Contains(term));
            }

            var items = await query
                .OrderByDescending(r => r.CreatedAtUtc)
                .Take(500)
                .ToListAsync(cancellationToken);

            var catalog = await DocumentChecklist.LoadCatalogAsync(db, cancellationToken);
            var rows = items.Select(r => PreAuthorizationRowDto.From(r, catalog)).ToList();
            return Results.Ok(new
            {
                count = rows.Count,
                authorizedCount = rows.Count(r => r.IsPreAuthorized),
                // Eligible = confirmed SIS, not yet cleared, and holding the papers required for
                // authorization. The remainder of the checklist is tracked for follow-up
                // (SubmittedCount/TotalCount) but does not gate clearance.
                eligibleCount = rows.Count(r => !r.IsPreAuthorized
                    && r.RegistrationStatus == nameof(Domain.RegistrationStatus.Confirmed)
                    && r.MissingAuthorizationRequirements.Count == 0),
                students = rows
            });
        }

        private static async Task<IResult> GrantAsync(
            Guid registrationId,
            AppDbContext db,
            AuditLog audit,
            ClaimsPrincipal principal,
            CancellationToken cancellationToken)
        {
            var registration = await db.StudentRegistrations
                .Include(r => r.Semester)
                .Include(r => r.Documents)
                .FirstOrDefaultAsync(r => r.Id == registrationId, cancellationToken);

            if (registration is null)
            {
                return Results.NotFound(new { message = "Registration not found." });
            }

            var catalog = await DocumentChecklist.LoadCatalogAsync(db, cancellationToken);
            if (registration.IsPreAuthorized)
            {
                return Results.Ok(PreAuthorizationRowDto.From(registration, catalog));
            }

            // Two preconditions. The SIS must be Registrar-confirmed, and the papers the catalog
            // marks as required for authorization must be in hand — the report card and good moral
            // for a new enrollee, the transcript and honorable dismissal for a transferee. The rest
            // of the checklist may still be arriving (a student can submit those even after
            // enlistment opens), so it is followed up on rather than blocked on.
            var blockers = new List<string>();
            if (registration.Status != RegistrationStatus.Confirmed)
            {
                blockers.Add($"The SIS registration is {registration.Status} — the Registrar must confirm it first.");
            }

            var missing = DocumentChecklist.MissingAuthorizationRequirements(
                DocumentChecklist.Applicable(registration, catalog), catalog);
            blockers.AddRange(missing.Select(name =>
                $"{name} has not been submitted — it is required before this student can be authorized."));

            if (blockers.Count > 0)
            {
                return Results.BadRequest(new
                {
                    message = "This student cannot be pre-authorized yet.",
                    reasons = blockers
                });
            }

            registration.IsPreAuthorized = true;
            registration.PreAuthorizedAtUtc = DateTime.UtcNow;
            registration.PreAuthorizedByUserId = CurrentUserId(principal);

            audit.Record(AuditAction.StudentPreAuthorized,
                $"Pre-authorized {registration.StudentNumber} ({registration.FullName}) for online subject enlistment.",
                "StudentRegistration", registration.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(PreAuthorizationRowDto.From(registration, catalog));
        }

        private static async Task<IResult> RevokeAsync(
            Guid registrationId,
            AppDbContext db,
            AuditLog audit,
            CancellationToken cancellationToken)
        {
            var registration = await db.StudentRegistrations
                .Include(r => r.Semester)
                .Include(r => r.Documents)
                .FirstOrDefaultAsync(r => r.Id == registrationId, cancellationToken);

            if (registration is null)
            {
                return Results.NotFound(new { message = "Registration not found." });
            }

            if (registration.IsPreAuthorized)
            {
                registration.IsPreAuthorized = false;
                registration.PreAuthorizedAtUtc = null;
                registration.PreAuthorizedByUserId = null;

                audit.Record(AuditAction.StudentPreAuthorized,
                    $"Revoked the enlistment pre-authorization of {registration.StudentNumber} ({registration.FullName}).",
                    "StudentRegistration", registration.Id.ToString());
                await db.SaveChangesAsync(cancellationToken);
            }

            var catalog = await DocumentChecklist.LoadCatalogAsync(db, cancellationToken);
            return Results.Ok(PreAuthorizationRowDto.From(registration, catalog));
        }

        private static Guid? CurrentUserId(ClaimsPrincipal principal) =>
            Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    }
}
