namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// How a subject is delivered. A lecture-laboratory subject is <b>two separate meetings</b>
    /// in the timetable — its lecture hours in a lecture room and its laboratory hours in the
    /// laboratory it requires — not one long block, so the engine schedules it as two components
    /// (FR-SCHED-02, FR-SCHED-04).
    /// </summary>
    public enum SubjectDelivery
    {
        LectureOnly = 0,
        LaboratoryOnly = 1,
        LectureLaboratory = 2
    }

    /// <summary>One of the two meetings a subject can contribute to the timetable.</summary>
    public enum ClassComponent
    {
        Lecture = 0,
        Laboratory = 1
    }

    public static class SubjectDeliveries
    {
        public static bool HasLecture(this SubjectDelivery delivery) =>
            delivery is SubjectDelivery.LectureOnly or SubjectDelivery.LectureLaboratory;

        public static bool HasLaboratory(this SubjectDelivery delivery) =>
            delivery is SubjectDelivery.LaboratoryOnly or SubjectDelivery.LectureLaboratory;

        public static string Label(this SubjectDelivery delivery) => delivery switch
        {
            SubjectDelivery.LaboratoryOnly => "Laboratory only",
            SubjectDelivery.LectureLaboratory => "Lecture–Laboratory",
            _ => "Lecture only"
        };

        /// <summary>Compact badge text used on the board, the curriculum list, and reports.</summary>
        public static string ShortLabel(this SubjectDelivery delivery) => delivery switch
        {
            SubjectDelivery.LaboratoryOnly => "LAB",
            SubjectDelivery.LectureLaboratory => "LEC-LAB",
            _ => "LEC"
        };
    }
}
