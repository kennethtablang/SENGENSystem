using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Paging;
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
            string? filter,
            int? page,
            int? pageSize,
            string? sort,
            string? dir,
            AppDbContext db,
            CancellationToken cancellationToken)
        {
            var query = db.StudentRegistrations
                .AsNoTracking()
                .Include(r => r.Semester)
                .Include(r => r.Documents)
                .AsQueryable();

            // Clearance is granted for a term, so the queue follows the active one — otherwise every
            // past term's enrollees pile up here after a rollover. A search widens to every term.
            if (string.IsNullOrWhiteSpace(search)
                && await db.GetActiveSemesterIdAsync(cancellationToken) is { } activeSemesterId)
            {
                query = query.Where(r => r.SemesterId == activeSemesterId);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(r =>
                    r.StudentNumber.Contains(term)
                    || r.LastName.Contains(term)
                    || r.FirstName.Contains(term));
            }

            // Which papers gate clearance. Fetched once so both the filter and the count below can
            // be expressed in SQL rather than by loading the queue and testing it in memory.
            var gatingCodes = await db.AdmissionRequirements.AsNoTracking()
                .Where(a => a.IsActive && a.IsRequiredForAuthorization)
                .Select(a => a.Code)
                .ToListAsync(cancellationToken);

            // The queue before the chip narrows it — the headline tallies are counted against this,
            // so switching the view cannot change what the term's outstanding work is reported as.
            var baseQuery = query;

            // Eligibility is derived from the checklist, so this used to be filtered in the browser
            // over the fetched rows. Paged, that would filter one page and page through a total
            // that counted the other two states as well.
            query = (filter?.ToLowerInvariant()) switch
            {
                "authorized" => query.Where(r => r.IsPreAuthorized),
                "eligible" => query.Where(r => !r.IsPreAuthorized
                    && r.Status == Domain.RegistrationStatus.Confirmed
                    && !r.Documents.Any(d =>
                        gatingCodes.Contains(d.RequirementCode) && d.Status == DocumentStatus.NotSubmitted)),
                "blocked" => query.Where(r => !r.IsPreAuthorized
                    && (r.Status != Domain.RegistrationStatus.Confirmed
                        || r.Documents.Any(d =>
                            gatingCodes.Contains(d.RequirementCode) && d.Status == DocumentStatus.NotSubmitted))),
                _ => query
            };

            var desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            var ordered = (sort?.ToLowerInvariant()) switch
            {
                "studentnumber" => desc
                    ? query.OrderByDescending(r => r.StudentNumber) : query.OrderBy(r => r.StudentNumber),
                "fullname" => desc
                    ? query.OrderByDescending(r => r.LastName).ThenByDescending(r => r.FirstName)
                    : query.OrderBy(r => r.LastName).ThenBy(r => r.FirstName),
                "program" => desc ? query.OrderByDescending(r => r.Program) : query.OrderBy(r => r.Program),
                "studenttype" => desc
                    ? query.OrderByDescending(r => r.StudentType) : query.OrderBy(r => r.StudentType),
                "registrationstatus" => desc
                    ? query.OrderByDescending(r => r.Status) : query.OrderBy(r => r.Status),
                "submittedcount" => desc
                    ? query.OrderByDescending(r => r.Documents.Count(d => d.Status != DocumentStatus.NotSubmitted))
                    : query.OrderBy(r => r.Documents.Count(d => d.Status != DocumentStatus.NotSubmitted)),
                "haslinkedaccount" => desc
                    ? query.OrderByDescending(r => r.UserId != null) : query.OrderBy(r => r.UserId != null),
                "authorization" => desc
                    ? query.OrderByDescending(r => r.IsPreAuthorized) : query.OrderBy(r => r.IsPreAuthorized),
                _ => query.OrderByDescending(r => r.CreatedAtUtc)
            };

            var paged = await ordered.ThenBy(r => r.Id)
                .ToPagedAsync(PageSpec.From(page, pageSize), cancellationToken);

            var catalog = await DocumentChecklist.LoadCatalogAsync(db, cancellationToken);
            var rows = paged.Items.Select(r => PreAuthorizationRowDto.From(r, catalog)).ToList();

            // Both tallies are counted in SQL over the whole queue — `baseQuery`, before the chip
            // narrows it — not over this page and not over the current view. Taken from the rows,
            // as they were before paging, "23 cleared" would have silently become "23 cleared on
            // this page", which is the number the Admission Officer works the queue by.
            //
            // Eligible = confirmed SIS, not yet cleared, and holding the papers that actually gate
            // authorization. The rest of the checklist is tracked for follow-up (SubmittedCount /
            // TotalCount) but does not gate clearance.
            var authorizedCount = await baseQuery.CountAsync(r => r.IsPreAuthorized, cancellationToken);

            var eligibleCount = await baseQuery.CountAsync(r =>
                !r.IsPreAuthorized
                && r.Status == Domain.RegistrationStatus.Confirmed
                && !r.Documents.Any(d =>
                    gatingCodes.Contains(d.RequirementCode)
                    && d.Status == DocumentStatus.NotSubmitted), cancellationToken);

            var body = new Paged<PreAuthorizationRowDto>(rows, paged.Total, paged.Page, paged.PageSize)
                .ToResponse("students");
            body["authorizedCount"] = authorizedCount;
            body["eligibleCount"] = eligibleCount;
            return Results.Ok(body);
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
