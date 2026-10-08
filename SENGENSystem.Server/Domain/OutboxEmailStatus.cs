namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// Where an <see cref="OutboxEmail"/> stands. Persisted as a string, so the names are the
    /// stable contract.
    /// </summary>
    public enum OutboxEmailStatus
    {
        /// <summary>Queued, not yet attempted — or attempted, failed, and awaiting a retry.</summary>
        Pending = 1,

        Sent = 2,

        /// <summary>
        /// Out of retries. Deliberately a terminal state rather than silent deletion: "we tried
        /// five times over an hour and this address never accepted it" is the answer someone needs
        /// when a student says they were never told, and a deleted row cannot give it.
        /// </summary>
        Failed = 3
    }
}
