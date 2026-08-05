using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Paging;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Audit.GetAuditTrail
{
    // Vertical slice: the School Admin reviews the accountability log (FR-AUD-01).
    // Read-only, newest-first, with an optional action filter and a bounded page size.
    public static class GetAuditTrailEndpoint
    {
        // The trail is read newest-first and scanned, not browsed, so its page is larger than the
        // 25-row default the work queues use.
        private const int DefaultPageSize = 50;

        public static IEndpointRouteBuilder MapGetAuditTrail(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/audit", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.SchoolAdmin)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            string? action,
            string? search,
            int? page,
            int? pageSize,
            string? sort,
            string? dir,
            AppDbContext db,
            CancellationToken cancellationToken)
        {
            var query = db.AuditEntries.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(action) && Enum.TryParse<AuditAction>(action, out var parsed))
            {
                query = query.Where(e => e.Action == parsed);
            }

            // Search moved to the server with the paging. The trail is the one list that grows
            // without bound, so it is where filtering only the rows the browser happened to receive
            // was most misleading — the further back an event was, the less findable it became.
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(e =>
                    (e.ActorName != null && e.ActorName.Contains(term))
                    || (e.Summary != null && e.Summary.Contains(term))
                    || (e.IpAddress != null && e.IpAddress.Contains(term)));
            }

            var desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            var ordered = (sort?.ToLowerInvariant()) switch
            {
                "actorname" => desc ? query.OrderByDescending(e => e.ActorName) : query.OrderBy(e => e.ActorName),
                "action" => desc ? query.OrderByDescending(e => e.Action) : query.OrderBy(e => e.Action),
                "summary" => desc ? query.OrderByDescending(e => e.Summary) : query.OrderBy(e => e.Summary),
                "ipaddress" => desc ? query.OrderByDescending(e => e.IpAddress) : query.OrderBy(e => e.IpAddress),
                "occurredatutc" => desc
                    ? query.OrderByDescending(e => e.OccurredAtUtc)
                    : query.OrderBy(e => e.OccurredAtUtc),
                _ => query.OrderByDescending(e => e.OccurredAtUtc)
            };

            var result = await ordered.ThenBy(e => e.Id)
                .ToPagedAsync(PageSpec.From(page, pageSize, DefaultPageSize), cancellationToken);

            // The action filter's options, taken from the whole trail rather than the page. The
            // client used to derive them from the rows it had, which paging would have reduced to
            // "the actions visible right now" — so filtering to anything not on the current page
            // would have become impossible, and the dropdown would reshuffle as you paged.
            var actions = await db.AuditEntries.AsNoTracking()
                .Select(e => e.Action)
                .Distinct()
                .ToListAsync(cancellationToken);

            var body = result.Select(AuditEntryDto.From).ToResponse("entries");
            body["actions"] = actions.Select(a => a.ToString()).OrderBy(a => a).ToList();
            return Results.Ok(body);
        }
    }
}
