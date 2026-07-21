namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// Institution-wide scheduling parameters the School Admin can tune (FR-SCHED-05).
    /// A single row: <see cref="SingletonId"/> keys it so the table can never grow a second
    /// competing configuration. Values that belong to an individual record stay on that record
    /// — per-faculty ceilings live on <see cref="FacultyProfile.MaxLoadUnits"/> and a section's
    /// own seat count on <see cref="Section.Capacity"/>; only the institutional defaults and
    /// ceilings live here.
    /// </summary>
    public class SystemSettings
    {
        /// <summary>The only valid primary key — there is exactly one settings row.</summary>
        public const int SingletonId = 1;

        public int Id { get; set; } = SingletonId;

        /// <summary>
        /// The institutional ceiling on a section's seat count (FR-ENL-03). New sections are
        /// created at this value and no section may be set above it. Lowering it never rewrites
        /// existing sections: a section whose seats are already taken must keep them, since
        /// dropping <see cref="Section.Capacity"/> below its EnrolledCount would violate the
        /// CK_Sections_EnrolledCount database constraint. Sections above a lowered cap are
        /// reported to the admin rather than silently changed.
        /// </summary>
        public int SectionCapacityCap { get; set; } = Section.DefaultCapacityCap;

        /// <summary>Audit breadcrumb: when the parameters were last touched, and by whom.</summary>
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public Guid? UpdatedByUserId { get; set; }
    }
}
