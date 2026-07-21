using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Scheduling.Engine
{
    // Pure inputs to the CSP engine, decoupled from EF so the solver stays unit-testable.

    /// <summary>
    /// A section that needs a (room, time slot) placement — one CSP variable.
    /// <para>
    /// The teaching faculty is <b>not</b> a decision variable. It arrives already decided from
    /// the Academic Head's load allocation (FR-FAC-01), which is what makes hard constraint H5
    /// — "faculty assigned only to qualified subjects" — structurally true rather than merely
    /// checked: the engine cannot invent an assignment the Head did not authorise.
    /// </para>
    /// </summary>
    public sealed record SectionVar(
        Guid SectionId,
        string SectionCode,
        string ProgramCode,
        string CohortKey,
        int Capacity,
        int Units,
        bool RequiresLaboratory,
        Guid FacultyProfileId,
        // Weekly contact minutes the section must occupy (Subject.Hours × 60). The base grid is
        // 90-minute periods, so a 3-hour subject must fill a *contiguous* run of periods rather
        // than a single one — this is what the search covers, and the Weekly Hours Tracker's basis.
        int RequiredMinutes);

    public sealed record RoomOption(Guid RoomId, int Capacity, bool IsLaboratory);

    /// <summary>A faculty member's preferred teaching window (soft constraint S1).</summary>
    public sealed record PreferredWindow(DayOfWeek Day, int StartMinutes, int EndMinutes)
    {
        public bool Contains(TimeSlot slot) =>
            Day == slot.Day && StartMinutes <= slot.StartMinutes && slot.EndMinutes <= EndMinutes;
    }

    public sealed record FacultyOption(
        Guid FacultyProfileId,
        string ProgramCode,
        int MaxLoadUnits,
        IReadOnlyList<PreferredWindow>? PreferredWindows = null,
        string? DisplayName = null)
    {
        public IReadOnlyList<PreferredWindow> Preferences => PreferredWindows ?? [];

        /// <summary>
        /// Who this is, for failure messages. A diagnostic that says "a faculty member is
        /// over their ceiling" without naming them cannot be acted on — the Academic Head
        /// would have to hunt through the whole allocation to find who.
        /// </summary>
        public string Label => string.IsNullOrWhiteSpace(DisplayName)
            ? $"Faculty {FacultyProfileId.ToString()[..8]}"
            : DisplayName!;
    }

    /// <summary>
    /// A concrete value chosen for a section variable: a room and a contiguous time block that
    /// covers the section's full weekly hours. <see cref="Slot"/> is the block itself (which may
    /// span several base periods), not a reference to a single stored period.
    /// </summary>
    public sealed record SectionAssignment(Guid SectionId, Guid RoomId, TimeSlot Slot, Guid FacultyProfileId);

    /// <summary>
    /// Relative importance of the soft constraints (S1–S2 plus room fit). Every term the
    /// scorer produces is normalised to 0..1 <i>before</i> weighting, so these numbers mean
    /// what they appear to mean — raising one genuinely raises that concern's influence.
    /// <para>
    /// That normalisation is the point: an earlier version summed raw room capacity (0..40+)
    /// with a preference term of ±8, so room-snugness silently swamped the constraints the
    /// institution actually cares about.
    /// </para>
    /// </summary>
    public sealed record SoftWeights(
        double Preference = 0.40,
        double IdleGap = 0.35,
        double RoomFit = 0.25)
    {
        public static readonly SoftWeights Default = new();

        /// <summary>Longest idle gap the normaliser treats as "as bad as it gets", in hours.</summary>
        public double GapSaturationHours { get; init; } = 8.0;
    }

    /// <summary>
    /// The complete constraint-satisfaction problem for one semester: the sections to place
    /// (variables) and the rooms and time slots they can occupy (domain values).
    /// </summary>
    public sealed class ScheduleProblem
    {
        public required IReadOnlyList<SectionVar> Sections { get; init; }

        public required IReadOnlyList<RoomOption> Rooms { get; init; }

        public required IReadOnlyList<TimeSlot> TimeSlots { get; init; }

        /// <summary>
        /// Faculty reference data — unit ceilings (hard constraint H4, validated before the
        /// search) and preferred windows (soft constraint S1, scored during it).
        /// </summary>
        public required IReadOnlyList<FacultyOption> Faculty { get; init; }

        public SoftWeights Weights { get; init; } = SoftWeights.Default;

        /// <summary>
        /// Randomisation seed. The engine is <i>deterministic given a seed</i>: it breaks ties
        /// between equally-good candidates (and equally-constrained sections) with a seeded shuffle,
        /// so a different seed explores the solution space differently and yields a different — but
        /// still conflict-free and equally-optimised — timetable. The caller passes a fresh seed
        /// per run for variety and records it, so any arrangement can be reproduced (FR-SCHED-08:
        /// reproducibility is preserved per seed rather than forcing one fixed output).
        /// </summary>
        public int Seed { get; init; }
    }
}
