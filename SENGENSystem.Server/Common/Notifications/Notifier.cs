using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Reports.Live;

namespace SENGENSystem.Server.Common.Notifications
{
    /// <summary>
    /// Cross-cutting writer for in-app bell notifications (FR-NOTIF). Like
    /// <see cref="Auditing.AuditLog"/>, slices stage notices here and the caller's own
    /// <c>SaveChangesAsync</c> persists them — a notice commits in the same transaction
    /// as the event it announces.
    /// </summary>
    public sealed class Notifier
    {
        private readonly AppDbContext _db;
        private readonly ReportsBroadcaster _broadcaster;

        public Notifier(AppDbContext db, ReportsBroadcaster broadcaster)
        {
            _db = db;
            _broadcaster = broadcaster;
        }

        public void Notify(Guid userId, NotificationKind kind, string title, string body, string? linkTo = null)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = userId,
                Kind = kind,
                Title = title,
                Body = body,
                LinkTo = linkTo
            });

            // Nudge open clients so the bell badge updates without waiting for its poll.
            // Best-effort and fire-and-forget; the client debounces past the commit.
            _broadcaster.Announce("notifications");
        }

        public void NotifyMany(IEnumerable<Guid> userIds, NotificationKind kind, string title, string body, string? linkTo = null)
        {
            foreach (var userId in userIds.Distinct())
            {
                Notify(userId, kind, title, body, linkTo);
            }
        }
    }
}
