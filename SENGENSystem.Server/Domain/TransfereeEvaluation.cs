namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// The Registrar's credit evaluation of a transferee against the curriculum they are entering
    /// (FR-EVAL-01/02). A transferee arrives with subjects already passed elsewhere; someone has to
    /// rule, subject by subject, which of those count here. That ruling decides two things at once:
    /// the subjects the student still has to take, and — from the units credited — the year level
    /// they enter at. Until it is <see cref="TransfereeEvaluationStatus.Completed"/> the transferee
    /// cannot enlist, because there is no honest answer yet to "which subjects are yours to take".
    /// <para>
    /// One evaluation per registration. Re-opening a completed evaluation keeps the same row and
    /// its decisions, so a correction is a revision rather than a fresh start.
    /// </para>
    /// </summary>
    public class TransfereeEvaluation
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid StudentRegistrationId { get; set; }

        public StudentRegistration? StudentRegistration { get; set; }

        /// <summary>
        /// The curriculum the decisions were made against. Pinned on the evaluation because a
        /// program can carry several effectivity versions — a sheet read against a different
        /// catalog than the one it was ruled on would be misleading.
        /// </summary>
        public Guid? CurriculumId { get; set; }

        public Curriculum? Curriculum { get; set; }

        public TransfereeEvaluationStatus Status { get; set; } = TransfereeEvaluationStatus.Pending;

        /// <summary>
        /// The year level the credited units earn, computed by <c>YearLevelPolicy</c> when the
        /// evaluation is completed. Kept beside <see cref="AssignedYearLevel"/> so a later override
        /// never erases what the curriculum actually implied.
        /// </summary>
        public int RecommendedYearLevel { get; set; } = 1;

        /// <summary>
        /// The year level actually given to the student on completion — the recommendation unless
        /// the Registrar overrode it. Copied onto <see cref="StudentRegistration.YearLevel"/>.
        /// </summary>
        public int AssignedYearLevel { get; set; } = 1;

        /// <summary>Free-text note from the Registrar, printed on the evaluation sheet.</summary>
        public string? Remarks { get; set; }

        public Guid? EvaluatedByUserId { get; set; }

        public DateTime? EvaluatedAtUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>One decision per curriculum subject considered.</summary>
        public ICollection<TransfereeEvaluationItem> Items { get; set; } = new List<TransfereeEvaluationItem>();
    }
}
