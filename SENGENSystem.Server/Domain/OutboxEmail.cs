namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// One email waiting to be sent, or the record of one that was.
    ///
    /// <para>
    /// Bulk email used to happen <i>inside</i> the HTTP request that triggered it — the reminder
    /// sweep and the bulk approval both looped over their targets calling SMTP synchronously. Three
    /// things were wrong with that, and this table fixes all three:
    /// </para>
    ///
    /// <list type="number">
    ///   <item><b>Unbounded work in a request.</b> A large term blocked the Registrar's button until
    ///     it finished or timed out, with no way to tell how far it had got.</item>
    ///   <item><b>No record of failure.</b> Successful dispatches were audited; a failure was
    ///     swallowed by design, so nobody could answer "did that student get the email?" A row here
    ///     carries its own status, attempt count, and last error.</item>
    ///   <item><b>Nothing was retried.</b> A transient SMTP outage silently lost the whole
    ///     sweep.</item>
    /// </list>
    ///
    /// <para>
    /// The property that makes this an <i>outbox</i> rather than a queue: a row is written in the
    /// same transaction as the change that justifies it. An email can therefore never be sent for a
    /// write that rolled back, and never lost for one that committed — which is exactly the
    /// guarantee a fire-and-forget <c>SendAsync</c> in the middle of a handler cannot give.
    /// </para>
    /// </summary>
    public class OutboxEmail
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string ToEmail { get; set; } = string.Empty;

        public string ToName { get; set; } = string.Empty;

        public string Subject { get; set; } = string.Empty;

        public string HtmlBody { get; set; } = string.Empty;

        public OutboxEmailStatus Status { get; set; } = OutboxEmailStatus.Pending;

        /// <summary>
        /// What produced this email — "DocumentReminder", "EnlistmentApproval". Free-form rather
        /// than an enum because the dispatcher never branches on it; it exists so a human reading
        /// the table can tell one sweep's rows from another's, and so
        /// <see cref="DedupeKey"/> has something meaningful to group by.
        /// </summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>
        /// Optional idempotency key. When set, the queue refuses to add a second pending row with
        /// the same key — so pressing "Send reminders" twice cannot email the same student twice
        /// while the first batch is still waiting to go out.
        /// </summary>
        public string? DedupeKey { get; set; }

        public int Attempts { get; set; }

        /// <summary>Why the last attempt failed. Null while pending or once sent.</summary>
        public string? LastError { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Not before this instant. Used for exponential backoff after a failure, so a dead mail
        /// server is retried on a widening interval rather than hammered every cycle.
        /// </summary>
        public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? SentAtUtc { get; set; }
    }
}
