namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// A prerequisite edge: <see cref="SubjectId"/> requires <see cref="PrerequisiteSubjectId"/>
    /// to have been taken first. Both subjects belong to the same curriculum. Feeds the
    /// curriculum-aware scheduling engine (FR-SCHED-04).
    /// </summary>
    public class SubjectPrerequisite
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>The subject that has the prerequisite.</summary>
        public Guid SubjectId { get; set; }

        public Subject? Subject { get; set; }

        /// <summary>The subject that must be taken first.</summary>
        public Guid PrerequisiteSubjectId { get; set; }

        public Subject? PrerequisiteSubject { get; set; }

        /// <summary>
        /// Whether an edge between these two subjects is a <b>co-requisite</b> rather than a
        /// prerequisite: both sit in the same year level and the same term of the curriculum, so
        /// the curriculum means them to be taken <i>together</i>, not one after the other.
        /// <para>
        /// The one definition every reader uses. The schedule generator reads such a pair as "offer
        /// both to every block" (FR-SCHED-04) and enlistment reads it as "met when passed or taken
        /// this term" (FR-ENL-16). If the two disagreed, the generator would build a timetable for
        /// a pair that enlistment then refused to let anyone take.
        /// </para>
        /// </summary>
        public static bool IsCoRequisitePair(Subject dependent, Subject prerequisite) =>
            dependent.YearLevel == prerequisite.YearLevel && dependent.Term == prerequisite.Term;
    }
}
