using Microsoft.Extensions.Configuration;
using SENGENSystem.Server.Common.Persistence;

namespace SENGENSystem.Server.Tests
{
    /// <summary>
    /// The two most powerful accounts on a fresh install are seeded with a password from
    /// configuration, or a default that sits in source control. The default must not survive the
    /// first sign-in; a configured password is the deployer's choice and is left alone.
    /// </summary>
    public class SeedPasswordTests
    {
        private static IConfiguration Config(string? value) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Seed:SuperAdminPassword"] = value })
                .Build();

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void An_unconfigured_seed_password_is_the_default_and_must_be_changed(string? configured)
        {
            var (password, mustChange) = DbInitializer.SeedPassword(Config(configured), "Seed:SuperAdminPassword");

            Assert.Equal("ChangeMe123!", password);
            Assert.True(mustChange);
        }

        [Fact]
        public void A_configured_seed_password_is_used_as_is()
        {
            var (password, mustChange) = DbInitializer.SeedPassword(Config("Deployer@Chosen1"), "Seed:SuperAdminPassword");

            Assert.Equal("Deployer@Chosen1", password);
            Assert.False(mustChange);
        }
    }
}
