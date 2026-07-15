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
    }
}
