using System.Security.Cryptography;
using System.Text;

namespace SENGENSystem.Server.Common.Auth
{
    /// <summary>
    /// One-time tokens for emailed links (password reset, email-change confirmation).
    /// The raw token travels only in the email; the database stores its SHA-256 hash,
    /// so leaked rows cannot be replayed as live links.
    /// </summary>
    public static class OneTimeToken
    {
        public static string Generate() =>
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        public static string Hash(string token) =>
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
