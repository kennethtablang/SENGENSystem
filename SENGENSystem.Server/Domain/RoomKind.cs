namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// What a room is equipped to host. This is a hard-constraint input to the scheduling
    /// engine: a subject's laboratory hours can only be placed in the matching laboratory,
    /// and lecture hours only in a lecture room (FR-SCHED-02, FR-SCHED-05).
    /// <para>
    /// The distinction matters because STI Alaminos has exactly one Computer Laboratory and
    /// one Kitchen Laboratory against numerous lecture rooms — a plain "is a lab" flag would
    /// let an ITP programming class be scheduled into the kitchen, and would let a pure
    /// lecture burn the one lab the whole campus shares.
    /// </para>
    /// </summary>
    public enum RoomKind
    {
        /// <summary>An ordinary classroom. Lecture hours go here, laboratory hours never do.</summary>
        LectureRoom = 0,

        /// <summary>Computer laboratory — required by ITP laboratory subjects.</summary>
        ComputerLaboratory = 1,

        /// <summary>Kitchen laboratory — required by HRA/HRS culinary laboratory subjects.</summary>
        KitchenLaboratory = 2
    }

    public static class RoomKinds
    {
        /// <summary>The laboratory kinds, i.e. everything that is not a plain lecture room.</summary>
        public static readonly RoomKind[] Laboratories =
            [RoomKind.ComputerLaboratory, RoomKind.KitchenLaboratory];

        public static bool IsLaboratory(this RoomKind kind) => kind != RoomKind.LectureRoom;

        /// <summary>Human label for lists, reports, and validation messages.</summary>
        public static string Label(this RoomKind kind) => kind switch
        {
            RoomKind.ComputerLaboratory => "Computer laboratory",
            RoomKind.KitchenLaboratory => "Kitchen laboratory",
            _ => "Lecture room"
        };
    }
}
