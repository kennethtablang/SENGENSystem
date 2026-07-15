using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Audit.GetAuditTrail
{
    // Vertical slice: the School Admin reviews the accountability log (FR-AUD-01).
    // Read-only, newest-first, with an optional action filter and a bounded page size.
    public static class GetAuditTrailEndpoint
    {
        private const int DefaultTake = 200;
        private const int MaxTake = 500;

        public static IEndpointRouteBuilder MapGetAuditTrail(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/audit", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.SchoolAdmin)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            string? action,
            int? take,
            AppDbContext db,
            CancellationToken cancellationToken)
        {
            var query = db.AuditEntries.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(action) && Enum.TryParse<AuditAction>(action, out var parsed))
            {
                query = query.Where(e => e.Action == parsed);
            }

            var pageSize = Math.Clamp(take ?? DefaultTake, 1, MaxTake);

            var entries = await query
                .OrderByDescending(e => e.OccurredAtUtc)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                count = entries.Count,
                entries = entries.Select(AuditEntryDto.From).ToList()
            });
        }
    }
}
