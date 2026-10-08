using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Common.Notifications
{
    /// <summary>
    /// Drains the email outbox in the background, so the volume that used to block a Registrar's
    /// button now happens after the response has already gone back.
    ///
    /// <para>
    /// In-process by design. A real job runner (Hangfire, a queue broker) would survive a restart
    /// mid-batch and scale past one machine, and for an institution this size neither is worth the
    /// operational weight — but the trade is real and worth naming: <b>if the process dies while a
    /// batch is in flight, those rows stay Pending and go out on the next start</b>, which is the
    /// safe direction. What this must never do is lose a row silently, and it does not: nothing is
    /// deleted, and every terminal state is recorded.
    /// </para>
    /// </summary>
    public sealed class OutboxDispatcher(
        IServiceProvider services,
        ILogger<OutboxDispatcher> logger) : BackgroundService
    {
        /// <summary>How often the outbox is checked when it was empty last time.</summary>
        private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Rows per cycle. Bounded so one enormous sweep cannot monopolise the SMTP connection or
        /// hold a database context open indefinitely — a backlog simply takes several cycles, which
        /// is exactly what a background job is for.
        /// </summary>
        private const int BatchSize = 25;

        /// <summary>
        /// Attempts before a row is given up on. Five attempts across the backoff below spans a bit
        /// over half an hour, which clears any transient outage without retrying a permanently bad
        /// address forever.
        /// </summary>
        private const int MaxAttempts = 5;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var sent = 0;
                try
                {
                    sent = await DrainOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // The loop must outlive any single failure — a dispatcher that dies on one bad
                    // cycle would silently stop all email for the lifetime of the process.
                    logger.LogError(ex, "Outbox dispatch cycle failed; retrying after the idle interval.");
                }

                // A full batch means there is probably more waiting, so go straight round again
                // rather than sleeping through a backlog.
                if (sent < BatchSize)
                {
                    try
                    {
                        await Task.Delay(IdleInterval, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        private async Task<int> DrainOnceAsync(CancellationToken ct)
        {
            // A scope per cycle: AppDbContext is scoped, and a BackgroundService is a singleton, so
            // resolving it directly would pin one context for the life of the process.
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

            var now = DateTime.UtcNow;
            var due = await db.OutboxEmails
                .Where(o => o.Status == OutboxEmailStatus.Pending && o.NextAttemptAtUtc <= now)
                .OrderBy(o => o.CreatedAtUtc)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (due.Count == 0) return 0;

            foreach (var mail in due)
            {
                if (ct.IsCancellationRequested) break;

                mail.Attempts++;
                EmailResult result;
                try
                {
                    result = await sender.SendAsync(mail.ToEmail, mail.ToName, mail.Subject, mail.HtmlBody, ct);
                }
                catch (Exception ex)
                {
                    // IEmailSender's contract says it should not throw for delivery failures, but a
                    // dispatcher that trusts that and is wrong loses the whole batch.
                    result = EmailResult.Failed($"{ex.GetType().Name}: {ex.Message}");
                }

                if (result.Sent)
                {
                    mail.Status = OutboxEmailStatus.Sent;
                    mail.SentAtUtc = DateTime.UtcNow;
                    mail.LastError = null;
                }
                else if (mail.Attempts >= MaxAttempts)
                {
                    mail.Status = OutboxEmailStatus.Failed;
                    mail.LastError = Truncate(result.Detail);
                    // Logged at warning rather than swallowed: this is the case nobody could see
                    // before, and it is the one someone will be asked about.
                    logger.LogWarning(
                        "Outbox email {Kind} to {Recipient} failed permanently after {Attempts} attempts: {Detail}",
                        mail.Kind, mail.ToEmail, mail.Attempts, result.Detail);
                }
                else
                {
                    mail.LastError = Truncate(result.Detail);
                    mail.NextAttemptAtUtc = DateTime.UtcNow.Add(Backoff(mail.Attempts));
                }
            }

            await db.SaveChangesAsync(ct);
            return due.Count;
        }

        /// <summary>1, 2, 4, 8 minutes — doubling, so a dead mail server is not hammered.</summary>
        private static TimeSpan Backoff(int attempts) =>
            TimeSpan.FromMinutes(Math.Pow(2, Math.Min(attempts, 4) - 1));

        private static string Truncate(string value) =>
            value.Length <= 500 ? value : value[..500];
    }
}
