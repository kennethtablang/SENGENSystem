using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Common.Auth
{
    /// <summary>
    /// Who may manage whose account — the one rule every user-management endpoint asks.
    /// <para>
    /// <b>You may act on an account, or hand out a role, only at or below your own rank.</b>
    /// Super Admin outranks School Admin, which outranks every other role. Before this, each
    /// endpoint carried its own guard, and every one of them protected the School Admin and
    /// none the Super Admin above it: an Academic Head could reset the Super Admin's password
    /// and sign in as them, change its email and claim it through "forgot password", deactivate
    /// it — or simply assign themselves the SuperAdmin role. Creation was the only path guarded.
    /// One rule here means a new endpoint, or a new role, cannot reopen that gap by omission.
    /// </para>
    /// <para>
    /// Rank is read from the caller's role claims after <see cref="SchoolAdminClaimsTransformation"/>,
    /// which grants a School Admin every role except SuperAdmin — so <c>IsInRole(SuperAdmin)</c>
    /// is true only for a real Super Admin, and <c>IsInRole(SchoolAdmin)</c> for either admin.
    /// </para>
    /// </summary>
    public static class AccountAuthority
    {
        public static int RankOf(UserRole role) => role switch
        {
            UserRole.SuperAdmin => 2,
            UserRole.SchoolAdmin => 1,
            _ => 0
        };

        public static int RankOf(ClaimsPrincipal caller) =>
            caller.IsInRole(nameof(UserRole.SuperAdmin)) ? 2
            : caller.IsInRole(nameof(UserRole.SchoolAdmin)) ? 1
            : 0;

        /// <summary>Null when allowed; otherwise the sentence to refuse with.</summary>
        public static string? RefuseManaging(ClaimsPrincipal caller, User target) =>
            RankOf(caller) >= RankOf(target.Role)
                ? null
                : $"Only a {Label(target.Role)} or higher can change a {Label(target.Role)} account.";

        /// <summary>Null when allowed; otherwise the sentence to refuse with.</summary>
        public static string? RefuseAssigning(ClaimsPrincipal caller, UserRole role) =>
            RankOf(caller) >= RankOf(role)
                ? null
                : $"Only a {Label(role)} or higher can give an account the {Label(role)} role.";

        /// <summary>
        /// Whether removing <paramref name="user"/> from its role (by demotion or deactivation)
        /// would leave that admin role with nobody active in it. Both admin roles have surfaces
        /// only they can reach — the survey for the Super Admin, institution setup for the School
        /// Admin — so the last one is never removed.
        /// </summary>
        public static async Task<bool> IsLastActiveAdminAsync(AppDbContext db, User user, CancellationToken ct) =>
            RankOf(user.Role) > 0
            && !await db.Users.AnyAsync(u => u.Role == user.Role && u.IsActive && u.Id != user.Id, ct);

        public static IResult Forbidden(string message) =>
            Results.Json(new { message }, statusCode: StatusCodes.Status403Forbidden);

        private static string Label(UserRole role) => role switch
        {
            UserRole.SuperAdmin => "Super Admin",
            UserRole.SchoolAdmin => "School Admin",
            _ => role.ToString()
        };
    }
}
