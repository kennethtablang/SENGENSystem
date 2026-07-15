using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

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
            AppDbContext db,
            CancellationToken cancellationToken)
        {
            var query = db.StudentRegistrations
                .AsNoTracking()
                .Include(r => r.Semester)
                .Include(r => r.Documents)
                .AsQueryable();

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

            var items = await query
                .OrderByDescending(r => r.CreatedAtUtc)
                .Take(500)
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                count = items.Count,
                registrations = items.Select(RegistrationListItemDto.From).ToList()
            });
        }
    }
}
