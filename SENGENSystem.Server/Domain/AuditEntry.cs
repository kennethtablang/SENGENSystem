namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// One accountability record in the audit trail (FR-AUD-01). Actor identity is captured
    /// as a snapshot (name + role at the time) as well as by id, so the log stays readable
    /// even if the account is later renamed, re-roled, or deactivated.
    /// </summary>
    public class AuditEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Null for actions taken before authentication exists (e.g. self-registration).</summary>
        public Guid? ActorUserId { get; set; }

        public string ActorName { get; set; } = string.Empty;

        public string ActorRole { get; set; } = string.Empty;

        public AuditAction Action { get; set; }

        /// <summary>Human-readable description of what happened.</summary>
        public string Summary { get; set; } = string.Empty;

        /// <summary>The kind of record affected (e.g. "User", "Semester"), when applicable.</summary>
        public string? EntityType { get; set; }

        /// <summary>Identifier of the affected record, when applicable.</summary>
        public string? EntityId { get; set; }

        /// <summary>Originating IP address, for security review.</summary>
        public string? IpAddress { get; set; }
    }
}
