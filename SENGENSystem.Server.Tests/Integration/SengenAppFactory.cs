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
        private string _connectionString = string.Empty;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
            builder.UseSetting("Email:Password", "");
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

        /// <summary>A client signed in as the given account, through the real login endpoint.</summary>
        public async Task<HttpClient> SignedInAsync(string email, string password)
        {
            var client = CreateClient();
            var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<LoginBody>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
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
