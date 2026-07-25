using Microsoft.EntityFrameworkCore;
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

            var items = await query
                .OrderByDescending(a => a.RequestedAtUtc)
                .Take(500)
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                count = items.Count,
                activations = items.Select(TermActivationDto.From).ToList()
            });
        }
    }
}
