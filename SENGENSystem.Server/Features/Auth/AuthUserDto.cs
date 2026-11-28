using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Auth
{
    public record AuthUserDto(
        Guid Id, string FirstName, string LastName, string Email, string Role,
        bool MustChangePassword, bool TwoFactorEnabled,
        // Present only when the user has a profile picture; changes whenever it does, so the
        // client fetches /api/profile/avatar?v=… once per picture and caches it.
        string? AvatarVersion)
    {
        public static AuthUserDto From(User user) =>
            new(user.Id, user.FirstName, user.LastName, user.Email, user.Role.ToString(),
                user.MustChangePassword, user.TwoFactorEnabled,
                user.AvatarUpdatedAtUtc?.Ticks.ToString());
    }
}
