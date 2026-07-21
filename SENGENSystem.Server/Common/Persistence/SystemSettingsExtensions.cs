using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Common.Persistence
{
    /// <summary>
    /// Shared read path for the singleton <see cref="SystemSettings"/> row. Scheduling and
    /// enlistment slices need the institutional caps without taking a dependency on the
    /// System Parameters feature, so the accessor lives next to the DbContext.
    /// </summary>
    public static class SystemSettingsExtensions
    {
        /// <summary>
        /// The settings row, or an unsaved default if seeding has not run yet. Callers that
        /// intend to write must use <see cref="GetSettingsForUpdateAsync"/> instead — the
        /// instance returned here is not tracked.
        /// </summary>
        public static async Task<SystemSettings> GetSettingsAsync(
            this AppDbContext db, CancellationToken ct = default)
        {
            return await db.SystemSettings.AsNoTracking()
                       .FirstOrDefaultAsync(s => s.Id == SystemSettings.SingletonId, ct)
                   ?? new SystemSettings();
        }

        /// <summary>
        /// The tracked settings row, created on first use so a database seeded before this
        /// feature existed still resolves to a real row rather than a phantom default.
        /// </summary>
        public static async Task<SystemSettings> GetSettingsForUpdateAsync(
            this AppDbContext db, CancellationToken ct = default)
        {
            var settings = await db.SystemSettings
                .FirstOrDefaultAsync(s => s.Id == SystemSettings.SingletonId, ct);
            if (settings is null)
            {
                settings = new SystemSettings();
                db.SystemSettings.Add(settings);
            }
            return settings;
        }

        /// <summary>The live ceiling on a section's seat count (FR-ENL-03).</summary>
        public static async Task<int> GetSectionCapacityCapAsync(
            this AppDbContext db, CancellationToken ct = default)
        {
            return (await db.GetSettingsAsync(ct)).SectionCapacityCap;
        }
    }
}
