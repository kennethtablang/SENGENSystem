namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// How far the Registrar has got with a transferee's credit evaluation (FR-EVAL-01).
    /// Persisted as a string — append, never renumber.
    /// </summary>
    public enum TransfereeEvaluationStatus
    {
        /// <summary>Opened but no decision recorded yet — the transferee is waiting on the Registrar.</summary>
        Pending = 1,

        /// <summary>Decisions are being recorded; the evaluation is not yet signed off.</summary>
        InProgress = 2,

        /// <summary>
        /// Signed off. The credited/to-take split is final, the year level has been assigned from
        /// it, and the transferee may enlist (FR-ENL-05).
        /// </summary>
        Completed = 3
    }
}
