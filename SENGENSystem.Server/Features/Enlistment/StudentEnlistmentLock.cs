using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SENGENSystem.Server.Common.Persistence;

namespace SENGENSystem.Server.Features.Enlistment
{
    /// <summary>
    /// Serializes one student's enlistment changes against each other.
    /// <para>
    /// The co-requisite rule is checked in one request and relied on in another. A seat request for
    /// a dependent subject passes because the co-requisite is still held, while a withdrawal of the
    /// co-requisite passes because the dependent is not held yet (<see cref="CoRequisiteGuard"/>).
    /// Sent at the same moment, both succeed and the pair is split. Each check is only true if the
    /// other request can't change the answer while this one is still deciding.
    /// </para>
    /// <para>
    /// So the paths that add to or take from a student's live set this term (request, cancel or
    /// drop, Registrar reject or drop) take an exclusive SQL Server application lock keyed on the
    /// student first. Requests for <i>different</i> students never wait on each other. The lock is
    /// held by a transaction and released when it ends.
    /// </para>
    /// <para>
    /// Committing on dispose is deliberate. Before this lock, each <c>SaveChangesAsync</c> in these
    /// handlers was durable on its own, including the audit row written on an early refusal. The
    /// transaction is here to serialize, not to make the handler all-or-nothing, so it keeps that
    /// behaviour. If the commit itself fails, disposing the transaction rolls it back.
    /// </para>
    /// <para>
    /// The in-memory provider used by the unit tests has no transactions or locks, so there the
    /// lock is a no-op. The integration tests run against SQL Server and do take it.
    /// </para>
    /// </summary>
    internal sealed class StudentEnlistmentLock : IAsyncDisposable
    {
        private const int TimeoutMilliseconds = 10_000;

        private readonly IDbContextTransaction? transaction;

        private StudentEnlistmentLock(IDbContextTransaction? transaction) => this.transaction = transaction;

        public static async Task<StudentEnlistmentLock> AcquireAsync(
            AppDbContext db, Guid registrationId, CancellationToken cancellationToken)
        {
            if (!db.Database.IsRelational() || db.Database.CurrentTransaction is not null)
            {
                // No lock to take, or a caller already owns the transaction (and decides its fate).
                return new StudentEnlistmentLock(null);
            }

            var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var resource = $"sengen:enlistment:{registrationId:N}";
                await db.Database.ExecuteSqlInterpolatedAsync($@"
DECLARE @result int;
EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive',
    @LockOwner = 'Transaction', @LockTimeout = {TimeoutMilliseconds};
IF @result < 0 THROW 50001, 'Another enlistment change for this student is still in progress. Try again.', 1;",
                    cancellationToken);
            }
            catch
            {
                await transaction.DisposeAsync();
                throw;
            }
            return new StudentEnlistmentLock(transaction);
        }

        public async ValueTask DisposeAsync()
        {
            if (transaction is null) return;
            try
            {
                await transaction.CommitAsync();
            }
            catch
            {
                // The transaction is already doomed (e.g. the connection dropped). Disposing it
                // rolls back; the original failure, if any, is the one worth surfacing.
            }
            finally
            {
                await transaction.DisposeAsync();
            }
        }
    }
}
