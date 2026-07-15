namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// A physical building on campus. It groups the teaching rooms it contains so the
    /// School Admin can manage spaces by structure (FR-SCHED room inputs, data §5).
    /// </summary>
    public class Building
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty; // e.g. "Main Building"

        /// <summary>Optional short code used in room labels and reports (e.g. "MB").</summary>
        public string? Code { get; set; }

        /// <summary>The rooms located in this building.</summary>
        public ICollection<Room> Rooms { get; set; } = new List<Room>();
    }
}
