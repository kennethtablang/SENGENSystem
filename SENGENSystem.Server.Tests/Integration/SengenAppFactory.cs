using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Tests.Integration
{
    /// <summary>
    /// The real application — its Program, middleware, auth, and endpoint wiring — against its own
    /// throwaway SQL Server database, seeded by the app's own <c>DbInitializer</c> on start.
    /// <para>
    /// Run in a "Testing" environment, which matters for one reason above all: in Development the
    /// app loads user-secrets, including the live SMTP password, and the outbox dispatcher would
    /// then send real mail to whatever the seed and the tests create. The password is also blanked
    /// explicitly, so a change to how secrets load cannot quietly re-arm it.
    /// </para>
    /// </summary>
    public sealed class SengenAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
    {
        public const string SuperAdminPassword = "Super@Test2026";

        private string _connectionString = string.Empty;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
            builder.UseSetting("Email:Password", "");
            // Secrets no longer live in appsettings.json (outside Development the app refuses to
            // start without a real signing key), so the test host supplies its own.
            builder.UseSetting("Jwt:Key", "test-only-signing-key-for-the-integration-suite-0123456789");
            builder.UseSetting("Seed:AdminPassword", "Admin@Sengen2026");
            // The suites arrange against the sample rooms, faculty and terms.
            builder.UseSetting("Seed:DemoData", "true");
            // A configured Super Admin password, as a real deployment has. Left unconfigured, the
            // seeded account would carry the must-change-password flag and the server would
            // (rightly) refuse it everything but the password change.
            builder.UseSetting("Seed:SuperAdminPassword", SuperAdminPassword);
        }

        public Task InitializeAsync()
        {
            if (!SqlServerTestDatabase.IsAvailable) return Task.CompletedTask;
            _connectionString = SqlServerTestDatabase.ConnectionStringFor(SqlServerTestDatabase.NewDatabaseName("app"));
            _ = Server; // start the host now: migrates and seeds the database
            return Task.CompletedTask;
        }

        public new async Task DisposeAsync()
        {
            if (string.IsNullOrEmpty(_connectionString)) return;
            await base.DisposeAsync();
            await using var db = SqlServerTestDatabase.NewContext(_connectionString);
            await db.Database.EnsureDeletedAsync();
        }

        /// <summary>A scoped DbContext on the app's own database, for arranging and asserting.</summary>
        public AsyncServiceScope Scope(out AppDbContext db)
        {
            var scope = Services.CreateAsyncScope();
            db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return scope;
        }

        // One login per account per run. The app limits sign-in attempts per address (20 a
        // minute), and every test client comes from the same one — signing in afresh for each
        // test tripped that limiter once the suite grew past twenty logins, which is the limiter
        // doing its job. Login itself is not what these tests are about.
        private readonly Dictionary<string, string> _tokens = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _tokenLock = new(1, 1);

        /// <summary>A client signed in as the given account. The first call per account goes through the real login endpoint.</summary>
        public async Task<HttpClient> SignedInAsync(string email, string password)
        {
            string token;
            await _tokenLock.WaitAsync();
            try
            {
                if (!_tokens.TryGetValue(email, out token!))
                {
                    var response = await CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });
                    response.EnsureSuccessStatusCode();
                    token = (await response.Content.ReadFromJsonAsync<LoginBody>())!.Token;
                    _tokens[email] = token;
                }
            }
            finally
            {
                _tokenLock.Release();
            }

            var client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        /// <summary>Creates an active Student account with a known password and returns its id.</summary>
        public async Task<Guid> CreateStudentUserAsync(string email, string password)
        {
            await using var scope = Scope(out var db);
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
            var user = new User
            {
                Email = email,
                FirstName = "Test",
                LastName = "Student",
                Role = UserRole.Student,
                IsActive = true
            };
            user.PasswordHash = hasher.HashPassword(user, password);
            db.Users.Add(user);
            await db.SaveChangesAsync();

            // Test students are created by the dozen; their tokens are minted by the app's own
            // JwtTokenService (the exact thing login issues) so they do not spend the login budget.
            var tokens = scope.ServiceProvider.GetRequiredService<SENGENSystem.Server.Common.Auth.JwtTokenService>();
            await _tokenLock.WaitAsync();
            try { _tokens[email] = tokens.CreateToken(user); }
            finally { _tokenLock.Release(); }
            return user.Id;
        }

        private sealed record LoginBody(string Token);
    }

    [CollectionDefinition(Name)]
    public sealed class AppCollection : ICollectionFixture<SengenAppFactory>
    {
        public const string Name = "SEN-GEN app";
    }
}
