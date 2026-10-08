using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;

namespace SENGENSystem.Server.Tests.Integration
{
    /// <summary>
    /// Where integration tests get a real SQL Server, and whether they can.
    /// <para>
    /// These exist for exactly the things <see cref="TestDb"/> says it cannot test: the
    /// <c>CK_Sections_EnrolledCount</c> check, the rowversion tokens, the filtered unique index —
    /// and query translation, which the in-memory provider never performs. That last one is not
    /// hypothetical: a seat-count query in pass 11 passed every in-memory test and then failed on
    /// the first live request.
    /// </para>
    /// <para>
    /// Resolution: the <c>SENGEN_TEST_SQL</c> environment variable (a server connection string —
    /// CI sets it to a SQL Server service container), otherwise LocalDB on Windows. With neither,
    /// every <see cref="SqlFactAttribute"/> test is reported <i>skipped</i> rather than passed, so
    /// the absence of a database can never read as coverage. Each fixture creates its own uniquely
    /// named database and drops it afterwards; the developer database is never touched.
    /// </para>
    /// </summary>
    internal static class SqlServerTestDatabase
    {
        private const string LocalDb =
            @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

        private static readonly Lazy<string?> Server = new(Resolve);

        public static bool IsAvailable => Server.Value is not null;

        public static string SkipReason =>
            "No SQL Server for integration tests — set SENGEN_TEST_SQL or install LocalDB.";

        /// <summary>A connection string for a fresh database name on the resolved server.</summary>
        public static string ConnectionStringFor(string database)
        {
            var builder = new SqlConnectionStringBuilder(Server.Value ?? throw new InvalidOperationException(SkipReason))
            {
                InitialCatalog = database,
                MultipleActiveResultSets = true
            };
            return builder.ConnectionString;
        }

        public static string NewDatabaseName(string purpose) => $"SENGEN_Test_{purpose}_{Guid.NewGuid():N}";

        public static AppDbContext NewContext(string connectionString) =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options);

        private static string? Resolve()
        {
            var fromEnv = Environment.GetEnvironmentVariable("SENGEN_TEST_SQL");
            var candidate = !string.IsNullOrWhiteSpace(fromEnv) ? fromEnv
                : OperatingSystem.IsWindows() ? LocalDb
                : null;
            if (candidate is null) return null;

            // A real round trip, once per run: "LocalDB is installed" and "LocalDB answers" are
            // different claims, and only the second one makes a test meaningful.
            try
            {
                var probe = new SqlConnectionStringBuilder(candidate) { InitialCatalog = "master", ConnectTimeout = 15 };
                using var connection = new SqlConnection(probe.ConnectionString);
                connection.Open();
                return candidate;
            }
            catch (SqlException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Skipping is right on a laptop with no database; it is wrong in CI, where a broken service
    /// container would otherwise turn every integration test into a silent skip and leave the
    /// build green. Setting <c>SENGEN_TEST_SQL</c> is a statement that a server is expected, so
    /// this one plain fact fails the run when that server cannot be reached.
    /// </summary>
    public class SqlServerAvailabilityTests
    {
        [Fact]
        public void A_configured_test_server_must_be_reachable()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SENGEN_TEST_SQL"))) return;
            Assert.True(SqlServerTestDatabase.IsAvailable,
                "SENGEN_TEST_SQL is set but the server did not answer — integration tests would all skip.");
        }
    }

    /// <summary>A fact that needs a real SQL Server; skipped (not passed) when there is none.</summary>
    public sealed class SqlFactAttribute : FactAttribute
    {
        public SqlFactAttribute()
        {
            if (!SqlServerTestDatabase.IsAvailable) Skip = SqlServerTestDatabase.SkipReason;
        }
    }

    /// <summary>
    /// One migrated, empty database shared by the database-level tests — migrating is the slow
    /// part, so it is done once. Tests stay independent by creating their own uniquely-coded rows.
    /// Migrated with the real migrations, not <c>EnsureCreated</c>, so the schema under test is the
    /// schema that ships.
    /// </summary>
    public sealed class MigratedDatabase : IAsyncLifetime
    {
        public string ConnectionString { get; private set; } = string.Empty;

        public AppDbContext NewContext() => SqlServerTestDatabase.NewContext(ConnectionString);

        public async Task InitializeAsync()
        {
            if (!SqlServerTestDatabase.IsAvailable) return;
            ConnectionString = SqlServerTestDatabase.ConnectionStringFor(SqlServerTestDatabase.NewDatabaseName("db"));
            await using var db = NewContext();
            await db.Database.MigrateAsync();
        }

        public async Task DisposeAsync()
        {
            if (string.IsNullOrEmpty(ConnectionString)) return;
            await using var db = NewContext();
            await db.Database.EnsureDeletedAsync();
        }
    }

    [CollectionDefinition(Name)]
    public sealed class SqlServerCollection : ICollectionFixture<MigratedDatabase>
    {
        public const string Name = "SQL Server";
    }
}
