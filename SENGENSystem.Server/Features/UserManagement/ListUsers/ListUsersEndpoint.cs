using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Paging;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.UserManagement.ListUsers
{
    // Vertical slice: the School Admin lists all accounts across the six roles (FR-AUTH-07),
    // with optional role/status filters and a free-text search over name and email.
    public static class ListUsersEndpoint
    {
        public static IEndpointRouteBuilder MapListUsers(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/users", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.SchoolAdmin), nameof(UserRole.AcademicHead)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            string? role,
            string? status,
            string? search,
            int? page,
            int? pageSize,
            string? sort,
            string? dir,
            AppDbContext db,
            CancellationToken cancellationToken)
        {
            var query = db.Users.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(role)
                && !string.Equals(role, "All", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<UserRole>(role, ignoreCase: true, out var parsedRole))
            {
                query = query.Where(u => u.Role == parsedRole);
            }

            if (string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(u => u.IsActive);
            }
            else if (string.Equals(status, "Inactive", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(u => !u.IsActive);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(u =>
                    u.FirstName.Contains(term)
                    || u.LastName.Contains(term)
                    || u.Email.Contains(term));
            }

            // Sorted in SQL, not in the browser. The page only ever holds one page of rows now, so
            // a client-side sort would order that window rather than the account list — the column
            // header would look like it worked and quietly lie.
            var desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            var ordered = (sort?.ToLowerInvariant()) switch
            {
                "fullname" => desc
                    ? query.OrderByDescending(u => u.LastName).ThenByDescending(u => u.FirstName)
                    : query.OrderBy(u => u.LastName).ThenBy(u => u.FirstName),
                "email" => desc ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email),
                "role" => desc ? query.OrderByDescending(u => u.Role) : query.OrderBy(u => u.Role),
                "status" => desc ? query.OrderByDescending(u => u.IsActive) : query.OrderBy(u => u.IsActive),
                "createdatutc" => desc
                    ? query.OrderByDescending(u => u.CreatedAtUtc)
                    : query.OrderBy(u => u.CreatedAtUtc),
                // The list's own order, and the tiebreaker under every sort above: two accounts
                // with the same surname must not swap places between page 1 and page 2.
                _ => query.OrderBy(u => u.Role).ThenBy(u => u.LastName)
            };

            var result = await ordered.ThenBy(u => u.Id)
                .ToPagedAsync(PageSpec.From(page, pageSize), cancellationToken);

            return Results.Ok(result.Select(UserDto.From).ToResponse("users"));
        }
    }
}
