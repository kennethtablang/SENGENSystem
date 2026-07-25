using Microsoft.EntityFrameworkCore;
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

            var users = await query
                .OrderBy(u => u.Role)
                .ThenBy(u => u.LastName)
                .Take(1000)
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                count = users.Count,
                users = users.Select(UserDto.From).ToList()
            });
        }
    }
}
