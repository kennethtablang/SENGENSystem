using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Paging;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Documents;
using SENGENSystem.Server.Features.Enlistment;

namespace SENGENSystem.Server.Features.Registration.Manage
{
    // Vertical slice: the Registrar reviews SIS submissions (FR-SIS-04). Newest first, with optional
    // status filter and a free-text search over student number and name.
    public static class ListRegistrationsEndpoint
    {
        public static IEndpointRouteBuilder MapListRegistrations(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/registration", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.Registrar)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            string? status,
            string? search,
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

            // Keep the review queue scoped to the current term so it stays correct and compact when
            // the semester rolls over — a search deliberately widens to every term so the Registrar
            // can still look up any past student by number or name.
            if (string.IsNullOrWhiteSpace(search)
                && await db.GetActiveSemesterIdAsync(cancellationToken) is { } activeSemesterId)
            {
                query = query.Where(r => r.SemesterId == activeSemesterId);
            }

            if (!string.IsNullOrWhiteSpace(status)
                && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<RegistrationStatus>(status, ignoreCase: true, out var parsed))
            {
                query = query.Where(r => r.Status == parsed);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(r =>
                    r.StudentNumber.Contains(term)
                    || r.LastName.Contains(term)
                    || r.FirstName.Contains(term));
            }

            var desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            var ordered = (sort?.ToLowerInvariant()) switch
            {
                "studentnumber" => desc
                    ? query.OrderByDescending(r => r.StudentNumber)
                    : query.OrderBy(r => r.StudentNumber),
                "fullname" => desc
                    ? query.OrderByDescending(r => r.LastName).ThenByDescending(r => r.FirstName)
                    : query.OrderBy(r => r.LastName).ThenBy(r => r.FirstName),
                "program" => desc ? query.OrderByDescending(r => r.Program) : query.OrderBy(r => r.Program),
                "studenttype" => desc
                    ? query.OrderByDescending(r => r.StudentType)
                    : query.OrderBy(r => r.StudentType),
                // Ordered by the count of papers actually handed in. Note this counts every
                // document row, while the number *displayed* counts only the ones that apply to
                // this enrollee's program and student type (DocumentChecklist.Applicable, which
                // needs the catalog and so cannot run in SQL). The two agree for anyone registered
                // since the catalog gained applicability rules; for older rows the ordering can be
                // a place or two off the printed figure. That is the right trade for a column whose
                // job is "who is furthest behind" rather than an exact ranking.
                "documentssubmitted" => desc
                    ? query.OrderByDescending(r => r.Documents.Count(d => d.Status != DocumentStatus.NotSubmitted))
                    : query.OrderBy(r => r.Documents.Count(d => d.Status != DocumentStatus.NotSubmitted)),
                "status" => desc ? query.OrderByDescending(r => r.Status) : query.OrderBy(r => r.Status),
                "createdatutc" => desc
                    ? query.OrderByDescending(r => r.CreatedAtUtc)
                    : query.OrderBy(r => r.CreatedAtUtc),
                _ => query.OrderByDescending(r => r.CreatedAtUtc)
            };

            var result = await ordered.ThenBy(r => r.Id)
                .ToPagedAsync(PageSpec.From(page, pageSize), cancellationToken);

            var catalog = await DocumentChecklist.LoadCatalogAsync(db, cancellationToken);

            // F-14, per row of this page only (at most PageSpec.MaxPageSize plan resolutions) —
            // the marker is derived from the plan, which cannot run in SQL, so it is not a filter.
            var completion = new Dictionary<Guid, EnrollmentCompletionDto>();
            foreach (var r in result.Items.Where(r => r.Status == RegistrationStatus.Confirmed && r.Semester is not null))
            {
                completion[r.Id] = await EnrollmentCompletion.EvaluateAsync(db, r, r.Semester!, cancellationToken);
            }

            var duplicates = await LikelyDuplicates.FindAsync(
                db, result.Items.Select(r => r.Id).ToList(), cancellationToken);

            return Results.Ok(result
                .Select(r => RegistrationListItemDto.From(r, catalog) with
                {
                    Enrollment = completion.GetValueOrDefault(r.Id),
                    LikelyDuplicateCount = duplicates.GetValueOrDefault(r.Id)?.Count ?? 0
                })
                .ToResponse("registrations"));
        }
    }
}
