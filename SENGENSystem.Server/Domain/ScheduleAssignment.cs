namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// The engine's output: a section placed into a room, time slot, and faculty member
    /// (FR-SCHED-01). Rows can be produced by the CSP engine or by a manual override
    /// (FR-FAC-02), and are published to students/faculty once finalized (FR-PUB).
    /// </summary>
    public class ScheduleAssignment
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid SemesterId { get; set; }

        public Guid SectionId { get; set; }

        public Section? Section { get; set; }

        public Guid RoomId { get; set; }

        public Room? Room { get; set; }

        public Guid TimeSlotId { get; set; }

        public TimeSlot? TimeSlot { get; set; }

        public Guid FacultyProfileId { get; set; }

        public FacultyProfile? FacultyProfile { get; set; }

        public bool IsPublished { get; set; }

        /// <summary>True when a human adjusted this row rather than the engine (FR-FAC-02).</summary>
        public bool IsManualOverride { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
