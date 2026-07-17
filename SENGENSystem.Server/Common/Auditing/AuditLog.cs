using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Reports.Live;

namespace SENGENSystem.Server.Common.Auditing
{
    /// <summary>
    /// Cross-cutting writer for the audit trail (FR-AUD-01). Slices call one of the
    /// <c>Record</c> methods to stage an entry; it is persisted by the caller's own
    /// <c>SaveChangesAsync</c>, so the audit row commits in the same transaction as the
    /// action it describes (never one without the other).
    /// </summary>
    public sealed class AuditLog
    {
        private readonly AppDbContext _db;
        private readonly IHttpContextAccessor _http;
        private readonly ReportsBroadcaster _broadcaster;

        public AuditLog(AppDbContext db, IHttpContextAccessor http, ReportsBroadcaster broadcaster)
        {
            _db = db;
            _http = http;
            _broadcaster = broadcaster;
        }

        /// <summary>Records an action by the current authenticated actor (resolved from the request).</summary>
        public void Record(AuditAction action, string summary, string? entityType = null, string? entityId = null)
        {
            var principal = _http.HttpContext?.User;

            Guid? actorId = null;
            var idValue = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if (Guid.TryParse(idValue, out var parsed))
            {
                actorId = parsed;
            }

            var name = principal?.Identity?.Name
                ?? principal?.FindFirstValue(ClaimTypes.Name)
                ?? principal?.FindFirstValue(JwtRegisteredClaimNames.Name)
                ?? "Unknown";
            var role = principal?.FindFirstValue(ClaimTypes.Role) ?? "Unknown";

            Add(action, summary, actorId, name, role, entityType, entityId);
        }

        /// <summary>
        /// Records an action attributed to a specific user, for actions that happen before a
        /// token exists (e.g. self-registration) or where the actor differs from the caller.
        /// </summary>
        public void RecordFor(User actor, AuditAction action, string summary, string? entityType = null, string? entityId = null)
        {
            Add(action, summary, actor.Id, actor.FullName, actor.Role.ToString(), entityType, entityId);
        }

        /// <summary>
        /// Records an action with no authenticated actor and no matching account, labelling the entry
        /// with a caller-supplied identifier (e.g. the email typed into a failed sign-in) so the trail
        /// still shows what was attempted. Prefer <see cref="Record"/> or <see cref="RecordFor"/> whenever
        /// a real account is known.
        /// </summary>
        public void RecordAnonymous(AuditAction action, string summary, string actorLabel, string? entityType = null, string? entityId = null)
        {
            Add(action, summary, null, actorLabel, "Anonymous", entityType, entityId);
        }

        private void Add(
            AuditAction action, string summary, Guid? actorId, string actorName, string actorRole,
            string? entityType, string? entityId)
        {
            _db.AuditEntries.Add(new AuditEntry
            {
                Action = action,
                Summary = summary,
                ActorUserId = actorId,
                ActorName = actorName,
                ActorRole = actorRole,
                EntityType = entityType,
                EntityId = entityId,
                IpAddress = _http.HttpContext?.Connection.RemoteIpAddress?.ToString()
            });

            // Every audited action nudges live listeners (audit trail + dashboard). Best-effort
            // and fire-and-forget; clients debounce, so the refetch lands after the commit.
            _broadcaster.Announce("audit");
        }
    }
}
