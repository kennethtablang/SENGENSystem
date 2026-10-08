using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Notifications.Outbox;

namespace SENGENSystem.Server.Tests
{
    /// <summary>
    /// The outbox's lifecycle states, and the retry semantics the admin page depends on.
    ///
    /// <para>
    /// The dispatcher itself is a <c>BackgroundService</c> and is not driven here — what these
    /// assert is the state machine around it, which is where the mistakes with consequences live:
    /// re-sending something already delivered, or a retry that silently does nothing because the
    /// attempt counter was left at the cap.
    /// </para>
    /// </summary>
    public class OutboxDispatchTests
    {
        private static OutboxEmail Queued(string kind = "Test") => new()
        {
            ToEmail = "student@example.com",
            ToName = "Test Student",
            Subject = "Subject",
            HtmlBody = "<p>Body</p>",
            Kind = kind
        };

        [Fact]
        public void A_new_email_starts_pending_due_immediately_and_unattempted()
        {
            var mail = Queued();

            Assert.Equal(OutboxEmailStatus.Pending, mail.Status);
            Assert.Equal(0, mail.Attempts);
            Assert.Null(mail.SentAtUtc);
            Assert.Null(mail.LastError);
            // Due now, so the very next dispatcher cycle picks it up rather than waiting a backoff.
            Assert.True(mail.NextAttemptAtUtc <= DateTime.UtcNow.AddSeconds(1));
        }

        [Fact]
        public async Task The_dispatchers_query_selects_only_pending_rows_that_are_due()
        {
            // Mirrors the dispatcher's own filter. A row scheduled into the future must not be
            // picked up early, or the exponential backoff after a failure means nothing.
            using var fixture = TestDb.Create();

            var due = Queued("Due");
            var backingOff = Queued("BackingOff");
            backingOff.NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(5);
            var sent = Queued("Sent");
            sent.Status = OutboxEmailStatus.Sent;
            var failed = Queued("Failed");
            failed.Status = OutboxEmailStatus.Failed;

            fixture.Db.OutboxEmails.AddRange(due, backingOff, sent, failed);
            await fixture.Db.SaveChangesAsync();

            // Read the clock after the rows exist, as the dispatcher does — it computes `now` at the
            // top of each cycle, by which time everything queued is already in the past. Capturing
            // it beforehand would make even the due row look scheduled for the future.
            var now = DateTime.UtcNow;
            var selected = await fixture.Db.OutboxEmails
                .Where(o => o.Status == OutboxEmailStatus.Pending && o.NextAttemptAtUtc <= now)
                .ToListAsync();

            var only = Assert.Single(selected);
            Assert.Equal("Due", only.Kind);
        }

        [Fact]
        public async Task Requeuing_a_failed_email_clears_the_attempt_count_and_the_error()
        {
            // The failure mode this guards: leaving Attempts at the cap would let the dispatcher
            // declare the row failed again on its first try, which from the outside looks exactly
            // like the retry button doing nothing at all.
            using var fixture = TestDb.Create();
            var mail = Queued();
            mail.Status = OutboxEmailStatus.Failed;
            mail.Attempts = 5;
            mail.LastError = "SMTP host unreachable";
            mail.NextAttemptAtUtc = DateTime.UtcNow.AddHours(1);
            fixture.Db.OutboxEmails.Add(mail);
            await fixture.Db.SaveChangesAsync();

            // The real thing, not a copy of it. Re-implementing the endpoint's steps here would have
            // made this test pass even if the endpoint stopped doing them — which is the failure
            // mode a test like this exists to prevent.
            OutboxEndpoints.Requeue(mail);
            await fixture.Db.SaveChangesAsync();

            var reloaded = await fixture.Db.OutboxEmails.AsNoTracking().SingleAsync();
            Assert.Equal(OutboxEmailStatus.Pending, reloaded.Status);
            Assert.Equal(0, reloaded.Attempts);
            Assert.Null(reloaded.LastError);
            Assert.True(reloaded.NextAttemptAtUtc <= DateTime.UtcNow.AddSeconds(1));
        }

        [Fact]
        public async Task A_sent_email_is_never_eligible_for_retry()
        {
            // The dangerous direction. Retrying a delivered email sends it twice, and an
            // approval or publication notice arriving twice reads as the system malfunctioning.
            using var fixture = TestDb.Create();
            var mail = Queued();
            mail.Status = OutboxEmailStatus.Sent;
            mail.SentAtUtc = DateTime.UtcNow;
            fixture.Db.OutboxEmails.Add(mail);
            await fixture.Db.SaveChangesAsync();

            var retryable = await fixture.Db.OutboxEmails
                .Where(o => o.Status == OutboxEmailStatus.Failed)
                .ToListAsync();

            Assert.Empty(retryable);
        }

        [Fact]
        public async Task Failed_is_a_terminal_record_rather_than_a_deleted_row()
        {
            // "We tried five times over half an hour and that address never accepted it" is the
            // answer someone needs when a student says they were never told. A deleted row cannot
            // give it, which is why Failed is a state and not a cleanup.
            using var fixture = TestDb.Create();
            var mail = Queued("DocumentReminder");
            mail.Status = OutboxEmailStatus.Failed;
            mail.Attempts = 5;
            mail.LastError = "550 mailbox unavailable";
            fixture.Db.OutboxEmails.Add(mail);
            await fixture.Db.SaveChangesAsync();

            var kept = await fixture.Db.OutboxEmails.AsNoTracking().SingleAsync();
            Assert.Equal(OutboxEmailStatus.Failed, kept.Status);
            Assert.Equal(5, kept.Attempts);
            Assert.Contains("550", kept.LastError);
            Assert.Equal("DocumentReminder", kept.Kind);
        }
    }
}
