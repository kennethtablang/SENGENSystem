namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// A physical teaching space. Capacity and attributes are hard-constraint inputs to
    /// the scheduling engine (FR-SCHED-02 room capacity, FR-SCHED-05).
    /// </summary>
    public class Room
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty; // e.g. "Room 301"

        public int Capacity { get; set; }

        /// <summary>Whether the room is a laboratory; subjects that require a lab can only be placed here.</summary>
        public bool IsLaboratory { get; set; }

        /// <summary>The building this room is located in (FR: Building contains Rooms).</summary>
        public Guid? BuildingId { get; set; }

        public Building? Building { get; set; }
    }
}
