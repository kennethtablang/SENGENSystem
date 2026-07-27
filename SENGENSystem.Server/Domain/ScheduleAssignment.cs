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

        /// <summary>
        /// True once the Academic Head has finalized the draft: it is signed off as ready to
        /// publish and locked from regeneration and board edits until reopened. Distinct from
        /// <see cref="IsPublished"/>, which is the Registrar making it official to students/faculty.
        /// The lifecycle is Draft → Finalized → Published (FR-SCHED-06, FR-PUB).
        /// </summary>
        public bool IsFinalized { get; set; }

        public DateTime? FinalizedAtUtc { get; set; }

        /// <summary>True when a human adjusted this row rather than the engine (FR-FAC-02).</summary>
        public bool IsManualOverride { get; set; }

        /// <summary>
        /// True once this row was changed <i>after</i> it was published (FR-PUB-04). A published
        /// class is a promise already emailed to faculty and enrolled students, so an edit to one
        /// is not an ordinary override — it is an amendment those people must be told about. The
        /// flag survives on the row so the board, the published view, and the reports can all show
        /// which classes moved after the fact.
        /// </summary>
        public bool IsAmended { get; set; }

        public DateTime? AmendedAtUtc { get; set; }

        public Guid? AmendedByUserId { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
