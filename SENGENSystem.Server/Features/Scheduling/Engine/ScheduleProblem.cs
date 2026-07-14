using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Scheduling.Engine
{
    // Pure inputs to the CSP engine, decoupled from EF so the solver stays unit-testable.

    /// <summary>A section that needs a (room, time slot, faculty) assignment — one CSP variable.</summary>
    public sealed record SectionVar(
        Guid SectionId,
        string SectionCode,
        string ProgramCode,
        string CohortKey,
        int Capacity,
        int Units,
        bool RequiresLaboratory);

    public sealed record RoomOption(Guid RoomId, int Capacity, bool IsLaboratory);

    public sealed record FacultyOption(Guid FacultyProfileId, string ProgramCode, int MaxLoadUnits);

    /// <summary>A concrete value chosen for a section variable.</summary>
    public sealed record SectionAssignment(Guid SectionId, Guid RoomId, Guid TimeSlotId, Guid FacultyProfileId);

    /// <summary>
    /// The complete constraint-satisfaction problem for one semester: the sections to place
    /// (variables) and the rooms, time slots, and faculty they can be assigned (domain values),
    /// per FR-SCHED-01/05.
    /// </summary>
    public sealed class ScheduleProblem
    {
        public required IReadOnlyList<SectionVar> Sections { get; init; }

        public required IReadOnlyList<RoomOption> Rooms { get; init; }

        public required IReadOnlyList<TimeSlot> TimeSlots { get; init; }

        public required IReadOnlyList<FacultyOption> Faculty { get; init; }
    }
}
