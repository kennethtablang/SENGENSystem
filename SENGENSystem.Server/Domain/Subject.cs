using System.ComponentModel.DataAnnotations.Schema;

namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// A curriculum subject/course. Units drive faculty-load accounting and the program/year
    /// place it within a fixed curriculum cohort (FR-SCHED-04, FR-SCHED-05, data §5).
    /// </summary>
    public class Subject
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Code { get; set; } = string.Empty;  // e.g. "CS101"

        public string Title { get; set; } = string.Empty;

        public int Units { get; set; }

        /// <summary>
        /// Total weekly contact hours that must be plotted on the schedule board — always
        /// <see cref="LectureHours"/> + <see cref="LaboratoryHours"/>, kept in sync on save.
        /// Distinct from <see cref="Units"/> (e.g. a 1-unit laboratory meets 3 hours a week).
        /// </summary>
        public int Hours { get; set; }

        /// <summary>
        /// Lecture only, laboratory only, or both. A lecture-laboratory subject is scheduled as
        /// two separate meetings, so the split below — not the total — is what the engine and the
        /// Weekly Hours Tracker work from.
        /// </summary>
        public SubjectDelivery Delivery { get; set; } = SubjectDelivery.LectureOnly;

        /// <summary>Weekly hours delivered as lecture, in a lecture room. 0 for a laboratory-only subject.</summary>
        public int LectureHours { get; set; }

        /// <summary>Weekly hours delivered as laboratory, in <see cref="LabRoomKind"/>. 0 for a lecture-only subject.</summary>
        public int LaboratoryHours { get; set; }

        /// <summary>
        /// Which laboratory the laboratory hours require — Computer or Kitchen. Null when the
        /// subject has no laboratory component. STI Alaminos has exactly one of each, so this is
        /// what decides whether an ITP class and an HRA class contend for the same room at all.
        /// </summary>
        public RoomKind? LabRoomKind { get; set; }

        public string ProgramCode { get; set; } = string.Empty; // e.g. "BSCS" (kept in sync with the curriculum's program)

        public int YearLevel { get; set; }

        /// <summary>The term (first/second semester) this subject is offered in within its year.</summary>
        public SemesterTerm Term { get; set; } = SemesterTerm.FirstSemester;

        /// <summary>
        /// Whether any part of this subject needs a laboratory. Derived from <see cref="Delivery"/>
        /// — kept for the read models (reports, dashboards, faculty load) that only need the
        /// yes/no answer, while the engine works from the delivery split.
        /// </summary>
        [NotMapped]
        public bool RequiresLaboratory => Delivery.HasLaboratory();

        /// <summary>
        /// Soft-retirement for curriculum changes: an archived subject stays for history
        /// (sections, loads, audit) but is hidden from new offerings and load assignment.
        /// </summary>
        public bool IsArchived { get; set; }

        public DateTime? ArchivedAtUtc { get; set; }

        /// <summary>Why it was retired, e.g. "Replaced by CS102 in the 2027 curriculum".</summary>
        public string? ArchiveReason { get; set; }

        /// <summary>The curriculum this subject belongs to.</summary>
        public Guid? CurriculumId { get; set; }

        public Curriculum? Curriculum { get; set; }

        /// <summary>The subjects that must be taken before this one (same curriculum).</summary>
        public ICollection<SubjectPrerequisite> Prerequisites { get; set; } = new List<SubjectPrerequisite>();

        /// <summary>Weekly hours required for one component of this subject.</summary>
        public int HoursFor(ClassComponent component) =>
            component == ClassComponent.Lecture ? LectureHours : LaboratoryHours;

        /// <summary>
        /// The single room kind a component may be placed in — the heart of the room-suitability
        /// hard constraint. Lecture hours go to a lecture room; laboratory hours to the laboratory
        /// the subject requires (falling back to the computer laboratory for legacy rows that
        /// never recorded which lab they need).
        /// </summary>
        public RoomKind RoomKindFor(ClassComponent component) =>
            component == ClassComponent.Lecture
                ? RoomKind.LectureRoom
                : LabRoomKind ?? RoomKind.ComputerLaboratory;

        /// <summary>The components this subject contributes to the timetable — one meeting each.</summary>
        public IEnumerable<ClassComponent> Components()
        {
            if (Delivery.HasLecture()) yield return ClassComponent.Lecture;
            if (Delivery.HasLaboratory()) yield return ClassComponent.Laboratory;
        }
    }
}
