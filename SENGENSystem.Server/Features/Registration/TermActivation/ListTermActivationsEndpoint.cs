using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Paging;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Registration.TermActivation
{
    // Vertical slice: the Admission Officer reviews the queue of returning-student term-activation
    // requests to validate (pre-authorization of returning students). Defaults to pending, newest first.
    public static class ListTermActivationsEndpoint
    {
        public static IEndpointRouteBuilder MapListTermActivations(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/registration/term-activation", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.AdmissionOfficer)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            string? status,
            int? page,
            int? pageSize,
            string? sort,
            string? dir,
            AppDbContext db,
            CancellationToken cancellationToken)
        {
            var query = db.TermActivations
                .AsNoTracking()
                .Include(a => a.StudentRegistration)
                .Include(a => a.Semester)
                .AsQueryable();

            // Validations are always for the current term, so scope to the active semester — the
            // queue stays correct and compact once the term rolls over instead of listing every
            // past term's activations.
            if (await db.GetActiveSemesterIdAsync(cancellationToken) is { } activeSemesterId)
            {
                query = query.Where(a => a.SemesterId == activeSemesterId);
            }

            // Default view is the pending queue; an explicit status filter can widen it.
            if (string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(a => a.Status == TermActivationStatus.Pending);
            }
            else if (!string.Equals(status, "All", StringComparison.OrdinalIgnoreCase)
                     && Enum.TryParse<TermActivationStatus>(status, ignoreCase: true, out var parsed))
            {
                query = query.Where(a => a.Status == parsed);
            }

            var desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            var ordered = (sort?.ToLowerInvariant()) switch
            {
                // Sorted on the number the student actually identifies themselves by, falling back
                // to the internal registration number — the same rule the column displays.
                "studentnumber" => desc
                    ? query.OrderByDescending(a => a.StudentRegistration!.OfficialStudentNumber
                        ?? a.StudentRegistration!.StudentNumber)
                    : query.OrderBy(a => a.StudentRegistration!.OfficialStudentNumber
                        ?? a.StudentRegistration!.StudentNumber),
                "studentname" => desc
                    ? query.OrderByDescending(a => a.StudentRegistration!.LastName)
                    : query.OrderBy(a => a.StudentRegistration!.LastName),
                "yearlevel" => desc
                    ? query.OrderByDescending(a => a.StudentRegistration!.YearLevel)
                    : query.OrderBy(a => a.StudentRegistration!.YearLevel),
                "program" => desc
                    ? query.OrderByDescending(a => a.StudentRegistration!.Program)
                    : query.OrderBy(a => a.StudentRegistration!.Program),
                "semestername" => desc
                    ? query.OrderByDescending(a => a.Semester!.Name) : query.OrderBy(a => a.Semester!.Name),
                "status" => desc ? query.OrderByDescending(a => a.Status) : query.OrderBy(a => a.Status),
                "requestedatutc" => desc
                    ? query.OrderByDescending(a => a.RequestedAtUtc) : query.OrderBy(a => a.RequestedAtUtc),
                _ => query.OrderByDescending(a => a.RequestedAtUtc)
            };

            var result = await ordered.ThenBy(a => a.Id)
                .ToPagedAsync(PageSpec.From(page, pageSize), cancellationToken);

            return Results.Ok(result.Select(TermActivationDto.From).ToResponse("activations"));
        }
    }
}
