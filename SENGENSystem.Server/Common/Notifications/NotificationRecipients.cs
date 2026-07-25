using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Common.Notifications
{
    /// <summary>
    /// Resolves the recipient sets for cross-cutting bell notices (FR-NOTIF). Registration and
    /// term-activation events are visible to the whole back office, so a notice fans out to every
    /// active staff account — the roles that oversee enrollment — rather than a single owner.
    /// </summary>
    public static class NotificationRecipients
    {
        private static readonly UserRole[] StaffRoles =
        [
            UserRole.Registrar,
            UserRole.AdmissionOfficer,
            UserRole.AcademicHead,
            UserRole.SchoolAdmin
        ];

        // The roles that decide and act when a section fills (open another section, raise the cap,
        // move students) — the Registrar, the Academic Head, and the overseeing School Admin.
        private static readonly UserRole[] DecisionMakerRoles =
        [
            UserRole.Registrar,
            UserRole.AcademicHead,
            UserRole.SchoolAdmin
        ];

        /// <summary>Active back-office staff who should see registration/term-activation notices.</summary>
        public static Task<List<Guid>> StaffUserIdsAsync(AppDbContext db, CancellationToken cancellationToken) =>
            db.Users.AsNoTracking()
                .Where(u => u.IsActive && StaffRoles.Contains(u.Role))
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);

        /// <summary>Active decision-makers notified when a section fills (Registrar, Academic Head, Admin).</summary>
        public static Task<List<Guid>> DecisionMakerUserIdsAsync(AppDbContext db, CancellationToken cancellationToken) =>
            db.Users.AsNoTracking()
                .Where(u => u.IsActive && DecisionMakerRoles.Contains(u.Role))
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);
    }
}
