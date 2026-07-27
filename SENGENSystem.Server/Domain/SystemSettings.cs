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

        // ---- Soft-constraint weights the CSP engine optimises against (FR-SCHED-03/-05) ----
        // Relative importance of each soft constraint; the engine normalises every term to 0..1
        // before weighting, so these numbers mean what they appear to mean. The defaults mirror
        // the engine's built-in SoftWeights so a fresh database behaves exactly as before this
        // became tunable. The Academic Head adjusts them before regenerating a timetable.

        /// <summary>How much honouring a faculty member's preferred teaching window matters (S1).</summary>
        public double WeightPreference { get; set; } = 0.40;

        /// <summary>How much minimising idle gaps between classes matters (S2).</summary>
        public double WeightIdleGap { get; set; } = 0.35;

        /// <summary>How much snugly fitting a section into a right-sized room matters.</summary>
        public double WeightRoomFit { get; set; } = 0.25;

        /// <summary>The longest idle gap the engine treats as "as bad as it gets", in hours.</summary>
        public double GapSaturationHours { get; set; } = 8.0;

        // ---- Enrollment / enlistment governance (FR-ENL) ----

        /// <summary>
        /// Institution-wide switch for online slot selection (FR-ENL). When false, students cannot
        /// file new slot requests regardless of their individual eligibility — the Registrar closes
        /// enlistment between periods without deactivating anyone.
        /// </summary>
        public bool EnlistmentOpen { get; set; } = true;

        /// <summary>
        /// The most subject units a single student may hold across their requested/approved sections
        /// in a term (FR-ENL). 0 means no institutional ceiling — only per-section capacity applies.
        /// </summary>
        public int MaxEnlistmentUnitsPerStudent { get; set; }

        /// <summary>
        /// The smallest enrollment a section should reach to be viable to run. Advisory: sections
        /// below it are surfaced to the admin (like sections above the seat cap) rather than blocked.
        /// </summary>
        public int MinSectionEnrollment { get; set; } = 15;

        // ---- Scheduling engine budgets (FR-SCHED-07) ----

        /// <summary>Wall-clock ceiling for one CSP generation run, in seconds (the engine's safety budget).</summary>
        public int ScheduleTimeBudgetSeconds { get; set; } = 20;

        /// <summary>Search-step ceiling for one CSP generation run, in thousands of steps.</summary>
        public int ScheduleMaxStepsThousands { get; set; } = 2000;

        /// <summary>
        /// The earliest a generated class may start, in minutes from midnight (FR-SCHED-05). The
        /// engine only builds its blocks from allowable periods at or after this time, so the
        /// institution decides when the teaching day opens without editing the period grid — a
        /// 07:30 start simply drops the 07:00 period from consideration. Manual board placements
        /// are unaffected; this governs generation.
        /// </summary>
        public int ClassDayStartMinutes { get; set; } = 7 * 60;

        /// <summary>Audit breadcrumb: when the parameters were last touched, and by whom.</summary>
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public Guid? UpdatedByUserId { get; set; }
    }
}
