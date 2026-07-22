using System.ComponentModel.DataAnnotations.Schema;

namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// A physical teaching space. Capacity and <see cref="Kind"/> are hard-constraint inputs to
    /// the scheduling engine (FR-SCHED-02 room capacity, FR-SCHED-05 room suitability).
    /// </summary>
    public class Room
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty; // e.g. "Room 301"

        public int Capacity { get; set; }

        /// <summary>
        /// What the room is equipped for. A subject's laboratory hours are placed only in a room
        /// of the laboratory kind they require; lecture hours only in a <see cref="RoomKind.LectureRoom"/>.
        /// </summary>
        public RoomKind Kind { get; set; } = RoomKind.LectureRoom;

        /// <summary>
        /// Convenience over <see cref="Kind"/> for the many read models that only care whether a
        /// room is a laboratory at all (reports, dashboards, utilization). Derived, not stored —
        /// the kind is the single source of truth.
        /// </summary>
        [NotMapped]
        public bool IsLaboratory => Kind.IsLaboratory();

        /// <summary>The building this room is located in (FR: Building contains Rooms).</summary>
        public Guid? BuildingId { get; set; }

        public Building? Building { get; set; }
    }
}
