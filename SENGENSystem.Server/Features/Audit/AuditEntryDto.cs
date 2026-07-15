using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Audit
{
    /// <summary>A readable audit-trail row for the School Admin's accountability view (FR-AUD-01).</summary>
    public record AuditEntryDto(
        Guid Id,
        string OccurredAtUtc,
        string Action,
        string ActorName,
        string ActorRole,
        string Summary,
        string? EntityType,
        string? EntityId,
        string? IpAddress)
    {
        public static AuditEntryDto From(AuditEntry e) =>
            new(
                e.Id,
                // Timestamps are stored UTC, but EF reads them back as Kind=Unspecified, which
                // makes ToString("o") omit the zone designator — the client would then parse the
                // value as local time. Re-stamp as UTC so the round-trip is unambiguous ("…Z"),
                // and let the client render it in Philippine time.
                DateTime.SpecifyKind(e.OccurredAtUtc, DateTimeKind.Utc).ToString("o"),
                e.Action.ToString(),
                e.ActorName,
                e.ActorRole,
                e.Summary,
                e.EntityType,
                e.EntityId,
                e.IpAddress);
    }
}
