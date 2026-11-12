using SENGENSystem.Server.Common.Auth;

namespace SENGENSystem.Server.Tests
{
    /// <summary>
    /// The signing key is the one secret that lets a stranger mint any account's token, so the app
    /// refuses to start on a key that is missing, too short, or the development key published in
    /// source control (outside Development).
    /// </summary>
    public class JwtKeyGuardTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("too-short-key")]
        public void A_missing_or_short_key_is_refused_everywhere(string? key)
        {
            Assert.Throws<InvalidOperationException>(() => JwtOptions.EnsureUsableKey(key, isDevelopment: true));
            Assert.Throws<InvalidOperationException>(() => JwtOptions.EnsureUsableKey(key, isDevelopment: false));
        }

        [Fact]
        public void The_published_development_key_is_refused_outside_development()
        {
            Assert.Throws<InvalidOperationException>(
                () => JwtOptions.EnsureUsableKey(JwtOptions.DevelopmentKey, isDevelopment: false));
        }

        [Fact]
        public void The_development_key_is_accepted_in_development()
        {
            JwtOptions.EnsureUsableKey(JwtOptions.DevelopmentKey, isDevelopment: true);
        }

        [Fact]
        public void A_real_key_is_accepted_anywhere()
        {
            var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
            JwtOptions.EnsureUsableKey(key, isDevelopment: false);
            JwtOptions.EnsureUsableKey(key, isDevelopment: true);
        }
    }
}
