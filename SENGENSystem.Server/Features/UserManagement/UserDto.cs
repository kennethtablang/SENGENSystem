using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.UserManagement
{
    /// <summary>A user account as seen in the School Admin's management console (FR-AUTH-07).</summary>
    public record UserDto(
        Guid Id,
        string FirstName,
        string LastName,
        string FullName,
        string Email,
        string Role,
        bool IsActive,
        string CreatedAtUtc,
        string? TermsAcceptedAtUtc)
    {
        public static UserDto From(User u) =>
            new(
                u.Id,
                u.FirstName,
                u.LastName,
                u.FullName,
                u.Email,
                u.Role.ToString(),
                u.IsActive,
                DateTime.SpecifyKind(u.CreatedAtUtc, DateTimeKind.Utc).ToString("o"),
                u.TermsAcceptedAtUtc is { } t ? DateTime.SpecifyKind(t, DateTimeKind.Utc).ToString("o") : null);
    }
}
