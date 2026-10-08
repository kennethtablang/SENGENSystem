namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// One subject a student has already sat for, and how it ended. This is SEN-GEN's academic
    /// history, and it exists because three things the system already claimed to do were resting
    /// on nothing without it:
    /// <list type="bullet">
    ///   <item><b>Prerequisites</b> were modeled, editable, and printed on the prospectus, but
    ///     could not be enforced — there was no way to ask whether a student had passed the
    ///     subject a prerequisite names.</item>
    ///   <item><b>Repeats</b> were never offered. The enlistment plan gave a student their year
    ///     level's subjects, so someone who failed last term was shown this term's list and never
    ///     the subject they still owed.</item>
    ///   <item><b>Year level</b> advanced on the calendar — one step per school year at term
    ///     activation — rather than on anything earned.</item>
    /// </list>
    ///
    /// <para>
    /// Deliberately <b>not</b> a grade record. There is no grade value, no scale, and no
    /// computation of standing: grading is a separate system's job, and a half-built version of it
    /// here would be worse than none. What is stored is the verdict a Registrar can defend from the
    /// paper record in front of them.
    /// </para>
    ///
    /// <para>
    /// Keyed by <see cref="StudentRegistrationId"/> + <see cref="SubjectId"/> + <see cref="SemesterId"/>
    /// rather than by student and subject alone, because a failed subject is retaken: the same
    /// student and subject legitimately appear more than once, in different terms, and the history
    /// has to keep both attempts. <see cref="StudentRegistration"/> is the student's persistent
    /// record — a returning student re-activates it rather than filing a new one — so it is the
    /// right anchor for something that spans terms.
    /// </para>
    /// </summary>
    public class StudentSubjectRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid StudentRegistrationId { get; set; }

        public StudentRegistration? StudentRegistration { get; set; }

        /// <summary>The curriculum subject that was taken.</summary>
        public Guid SubjectId { get; set; }

        public Subject? Subject { get; set; }

        /// <summary>The term it was taken in. Two attempts at one subject differ only by this.</summary>
        public Guid SemesterId { get; set; }

        public Semester? Semester { get; set; }

        public SubjectVerdict Verdict { get; set; } = SubjectVerdict.Passed;

        /// <summary>
        /// Optional note from the Registrar — where the record came from, or why a verdict was
        /// corrected. Free text, because the reasons are not enumerable.
        /// </summary>
        public string? Remarks { get; set; }

        /// <summary>Who recorded it and when — the provenance a checklist row still lacks (F-06).</summary>
        public Guid? RecordedByUserId { get; set; }

        public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Last edit, so a corrected verdict is distinguishable from an original one.</summary>
        public DateTime? UpdatedAtUtc { get; set; }
    }
}
