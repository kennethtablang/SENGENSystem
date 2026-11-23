using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Tests
{
    /// <summary>
    /// The School Admin seeded on first start. A deployment may not have chosen that person yet,
    /// and the Super Admin can create one later from User management, so a blank
    /// <c>Seed:AdminEmail</c> means "none yet". Seeding anyway would leave a powerful account at a
    /// placeholder address nobody can receive mail at.
    /// </summary>
    public class SeedAdminTests
    {
        // Read through the JSON provider, the way appsettings.Production.json is, so "" is tested
        // exactly as a deployer writes it.
        private static IConfiguration Json(string json) =>
            new ConfigurationBuilder()
                .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
                .Build();

        private static async Task<List<User>> SchoolAdminsAfterSeedingAsync(IConfiguration config)
        {
            using var fixture = TestDb.Create();
            await DbInitializer.SeedAdminAsync(fixture.Db, new PasswordHasher<User>(), config);
            return await fixture.Db.Users.Where(u => u.Role == UserRole.SchoolAdmin).ToListAsync();
        }

        [Theory]
        [InlineData("""{ "Seed": { "AdminEmail": "" } }""")]
        [InlineData("""{ "Seed": { "AdminEmail": "   " } }""")]
        public async Task A_blank_admin_email_seeds_no_school_admin(string json)
        {
            Assert.Empty(await SchoolAdminsAfterSeedingAsync(Json(json)));
        }

        [Fact]
        public async Task A_configured_admin_email_seeds_that_school_admin()
        {
            var admins = await SchoolAdminsAfterSeedingAsync(
                Json("""{ "Seed": { "AdminEmail": "registrar.head@school.example", "AdminPassword": "Chosen@Pass1" } }"""));

            var admin = Assert.Single(admins);
            Assert.Equal("registrar.head@school.example", admin.Email);
            Assert.False(admin.MustChangePassword);
        }

        [Fact]
        public async Task A_missing_admin_email_keeps_the_development_fallback()
        {
            var admin = Assert.Single(await SchoolAdminsAfterSeedingAsync(Json("{}")));

            Assert.Equal("admin@stialaminos.local", admin.Email);
            Assert.True(admin.MustChangePassword); // no password configured, so the default must be changed
        }
    }
}
