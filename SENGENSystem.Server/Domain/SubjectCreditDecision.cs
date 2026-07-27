namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// The Registrar's verdict on one curriculum subject during a transferee evaluation
    /// (FR-EVAL-01). Persisted as a string — append, never renumber.
    /// </summary>
    public enum SubjectCreditDecision
    {
        /// <summary>Not yet decided — the subject is on the sheet but the Registrar has not ruled.</summary>
        Undecided = 1,

        /// <summary>Credited from the previous school; the transferee does not retake it.</summary>
        Credited = 2,

        /// <summary>Not credited — the transferee must take this subject here.</summary>
        ToTake = 3
    }
}
