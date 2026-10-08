using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Common.Notifications
{
    /// <summary>
    /// Queues email instead of sending it. The counterpart to <see cref="Notifier"/>, which does the
    /// same thing for in-app bell notices — both stage rows on the caller's <see cref="AppDbContext"/>
    /// and let the caller's own <c>SaveChangesAsync</c> commit them.
    ///
    /// <para>
    /// That shared transaction is the entire point and is worth being explicit about. A handler that
    /// calls <c>IEmailSender.SendAsync</c> mid-flight has already sent the email by the time it
    /// decides whether to commit; if the save then fails, a student has been told about something
    /// that did not happen. Queueing here inverts that: no commit, no email.
    /// </para>
    ///
    /// <para>
    /// <see cref="IEmailSender"/> is <b>not</b> replaced by this. A single interactive email — a
    /// password reset, a two-factor code — should still go out inline, because the user is sitting
    /// there waiting for it and a queue would add latency to the one case where latency is the whole
    /// experience. The outbox is for the bulk and background paths, where nobody is waiting and the
    /// volume is what causes the harm.
    /// </para>
    /// </summary>
    public sealed class EmailOutbox(AppDbContext db)
    {
        /// <summary>
        /// Stages one email. Returns false when an identical <paramref name="dedupeKey"/> is already
        /// queued, so the caller can report how many were actually added rather than how many it
        /// asked for.
        /// </summary>
        public bool Queue(
            string toEmail, string toName, string subject, string htmlBody,
            string kind, string? dedupeKey = null)
        {
            if (string.IsNullOrWhiteSpace(toEmail)) return false;

            if (dedupeKey is not null && IsAlreadyQueued(dedupeKey))
            {
                return false;
            }

            db.OutboxEmails.Add(new OutboxEmail
            {
                ToEmail = toEmail.Trim(),
                ToName = toName,
                Subject = subject,
                HtmlBody = htmlBody,
                Kind = kind,
                DedupeKey = dedupeKey
            });
            return true;
        }

        /// <summary>
        /// Checks both the database and the rows staged on this context but not yet saved. The
        /// second half matters more than it looks: within a single sweep every row is unsaved, so a
        /// database-only check would let the same student be queued twice by one request — which is
        /// the exact duplicate the dedupe key exists to prevent.
        /// </summary>
        private bool IsAlreadyQueued(string dedupeKey)
        {
            var staged = db.ChangeTracker.Entries<OutboxEmail>()
                .Any(e => e.State == EntityState.Added && e.Entity.DedupeKey == dedupeKey);
            if (staged) return true;

            return db.OutboxEmails.Any(o =>
                o.DedupeKey == dedupeKey && o.Status == OutboxEmailStatus.Pending);
        }
    }
}
