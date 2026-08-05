namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// Lifecycle of a <see cref="SlotRequest"/> (FR-ENL-04): a student requests a seat, the
    /// Registrar approves or rejects it; students may cancel their own pending requests, and an
    /// approved seat may be given back (<see cref="Dropped"/>).
    /// <para>
    /// Only <see cref="Requested"/> and <see cref="Approved"/> are <i>live</i> — they hold a place
    /// in the student's load and are what the duplicate-subject, unit-ceiling, and overlap checks
    /// count. The three terminal states do not, which is why the filtered unique index on
    /// (student, section) covers only the live pair: a student who drops a subject may request it
    /// again.
    /// </para>
    /// </summary>
    public enum SlotRequestStatus
    {
        Requested = 1,
        Approved = 2,
        Rejected = 3,
        Cancelled = 4,

        /// <summary>
        /// An <see cref="Approved"/> seat given back — by the student while enlistment is open, or
        /// by staff correcting a mis-approval. Distinct from <see cref="Cancelled"/> (which a
        /// student does to a request that was never approved) because only this one returns a seat
        /// to <see cref="Section.EnrolledCount"/>.
        /// </summary>
        Dropped = 5
    }
}
