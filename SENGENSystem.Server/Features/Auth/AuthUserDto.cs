using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Auth
{
    public record AuthUserDto(
        Guid Id, string FirstName, string LastName, string Email, string Role,
        bool MustChangePassword, bool TwoFactorEnabled)
    {
        public static AuthUserDto From(User user) =>
            new(user.Id, user.FirstName, user.LastName, user.Email, user.Role.ToString(),
                user.MustChangePassword, user.TwoFactorEnabled);
    }
}
