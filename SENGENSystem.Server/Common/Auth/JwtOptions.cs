namespace SENGENSystem.Server.Common.Auth
{
    public class JwtOptions
    {
        public const string SectionName = "Jwt";

        public string Issuer { get; set; } = string.Empty;

        public string Audience { get; set; } = string.Empty;

        public string Key { get; set; } = string.Empty;

        public int ExpiryMinutes { get; set; } = 480;

        /// <summary>HMAC-SHA256 wants at least 256 bits of key; anything shorter is refused.</summary>
        public const int MinimumKeyLength = 32;

        /// <summary>
        /// The development-only signing key in <c>appsettings.Development.json</c>. It is in source
        /// control, so anyone can mint a valid token with it — it is accepted in Development and
        /// nowhere else.
        /// </summary>
        public const string DevelopmentKey = "CHANGE-THIS-DEV-ONLY-SIGNING-KEY-32+CHARS-LONG-0123456789";

        /// <summary>
        /// Refuses to start on a signing key that would let a stranger forge tokens: a missing or
        /// short one anywhere, and the published development key outside Development. The real key
        /// belongs in an environment variable (<c>Jwt__Key</c>), user-secrets, or a secrets
        /// manager — never in <c>appsettings.json</c>.
        /// </summary>
        public static void EnsureUsableKey(string? key, bool isDevelopment)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length < MinimumKeyLength)
            {
                throw new InvalidOperationException(
                    $"Jwt:Key must be set to a random secret of at least {MinimumKeyLength} characters. " +
                    "Supply it through the Jwt__Key environment variable or user-secrets, not appsettings.json.");
            }

            if (!isDevelopment && key == DevelopmentKey)
            {
                throw new InvalidOperationException(
                    "Jwt:Key is the development key from source control. Generate a fresh secret for this " +
                    "environment and supply it through the Jwt__Key environment variable or a secrets manager.");
            }
        }
    }
}
