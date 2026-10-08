using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Tests
{
    /// <summary>
    /// The queue half of the outbox. What is asserted here is the behaviour that makes it an outbox
    /// rather than a fire-and-forget send: nothing leaves until the caller commits, and the same
    /// email cannot be queued twice.
    /// </summary>
    public class EmailOutboxTests
    {
        [Fact]
        public void A_queued_email_is_not_persisted_until_the_caller_commits()
        {
            // The property the whole design rests on. If this ever inverts, a handler that queues
            // and then fails would have told a student about something that did not happen.
            using var fixture = TestDb.Create();
            var outbox = new EmailOutbox(fixture.Db);

            outbox.Queue("a@example.com", "A", "Subject", "<p>Body</p>", kind: "Test");

            Assert.Empty(fixture.Db.OutboxEmails.AsNoTracking().ToList());

            fixture.Db.SaveChanges();

            var mail = Assert.Single(fixture.Db.OutboxEmails.AsNoTracking().ToList());
            Assert.Equal(OutboxEmailStatus.Pending, mail.Status);
            Assert.Equal(0, mail.Attempts);
        }

        [Fact]
        public void A_duplicate_key_is_refused_within_one_unsaved_batch()
        {
            // The case a database-only check would miss: inside one sweep every row is still
            // unsaved, so without inspecting the change tracker the same student would be queued
            // twice by a single request — the exact duplicate the key exists to prevent.
            using var fixture = TestDb.Create();
            var outbox = new EmailOutbox(fixture.Db);

            Assert.True(outbox.Queue("a@example.com", "A", "S", "B", "Test", dedupeKey: "k1"));
            Assert.False(outbox.Queue("a@example.com", "A", "S", "B", "Test", dedupeKey: "k1"));

            fixture.Db.SaveChanges();
            Assert.Single(fixture.Db.OutboxEmails.AsNoTracking().ToList());
        }

        [Fact]
        public void A_duplicate_key_is_refused_against_already_pending_rows()
        {
            using var fixture = TestDb.Create();
            var outbox = new EmailOutbox(fixture.Db);
            outbox.Queue("a@example.com", "A", "S", "B", "Test", dedupeKey: "k1");
            fixture.Db.SaveChanges();

            Assert.False(outbox.Queue("a@example.com", "A", "S", "B", "Test", dedupeKey: "k1"));
        }

        [Fact]
        public void A_key_may_be_reused_once_its_earlier_email_has_been_sent()
        {
            // Dedupe is over *pending* rows only. Next term's reminder to the same student is a new
            // email, not a duplicate of one delivered months ago.
            using var fixture = TestDb.Create();
            var outbox = new EmailOutbox(fixture.Db);
            outbox.Queue("a@example.com", "A", "S", "B", "Test", dedupeKey: "k1");
            fixture.Db.SaveChanges();

            var sent = fixture.Db.OutboxEmails.Single();
            sent.Status = OutboxEmailStatus.Sent;
            sent.SentAtUtc = DateTime.UtcNow;
            fixture.Db.SaveChanges();

            Assert.True(outbox.Queue("a@example.com", "A", "S", "B", "Test", dedupeKey: "k1"));
        }

        [Fact]
        public void Distinct_keys_queue_independently()
        {
            using var fixture = TestDb.Create();
            var outbox = new EmailOutbox(fixture.Db);

            Assert.True(outbox.Queue("a@example.com", "A", "S", "B", "Test", dedupeKey: "k1"));
            Assert.True(outbox.Queue("b@example.com", "B", "S", "B", "Test", dedupeKey: "k2"));

            fixture.Db.SaveChanges();
            Assert.Equal(2, fixture.Db.OutboxEmails.AsNoTracking().Count());
        }

        [Fact]
        public void An_email_with_no_recipient_is_refused_rather_than_queued()
        {
            // A registration with a blank email would otherwise create a row the dispatcher retries
            // five times and then records as permanently failed, which is noise, not information.
            using var fixture = TestDb.Create();
            var outbox = new EmailOutbox(fixture.Db);

            Assert.False(outbox.Queue("", "A", "S", "B", "Test"));
            Assert.False(outbox.Queue("   ", "A", "S", "B", "Test"));

            fixture.Db.SaveChanges();
            Assert.Empty(fixture.Db.OutboxEmails.AsNoTracking().ToList());
        }

        [Fact]
        public void Without_a_key_the_same_email_may_be_queued_more_than_once()
        {
            // Dedupe is opt-in. Two genuinely distinct events that happen to produce identical text
            // must both go out; only the callers that know a repeat would be wrong pass a key.
            using var fixture = TestDb.Create();
            var outbox = new EmailOutbox(fixture.Db);

            Assert.True(outbox.Queue("a@example.com", "A", "S", "B", "Test"));
            Assert.True(outbox.Queue("a@example.com", "A", "S", "B", "Test"));

            fixture.Db.SaveChanges();
            Assert.Equal(2, fixture.Db.OutboxEmails.AsNoTracking().Count());
        }
    }
}
