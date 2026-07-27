namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// One curriculum subject's verdict within a <see cref="TransfereeEvaluation"/> (FR-EVAL-01).
    /// A credited row records <i>what it was credited from</i> — the previous school's subject and
    /// the grade earned — because a credit nobody can trace back is a credit nobody can defend.
    /// </summary>
    public class TransfereeEvaluationItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid TransfereeEvaluationId { get; set; }

        public TransfereeEvaluation? TransfereeEvaluation { get; set; }

        /// <summary>The subject in <i>this</i> school's curriculum being ruled on.</summary>
        public Guid SubjectId { get; set; }

        public Subject? Subject { get; set; }

        public SubjectCreditDecision Decision { get; set; } = SubjectCreditDecision.Undecided;

        /// <summary>The previous school's subject the credit came from, e.g. "IT100 — Intro to IT".</summary>
        public string? SourceSubject { get; set; }

        /// <summary>The grade earned there, as written on the transcript (e.g. "2.00", "85").</summary>
        public string? SourceGrade { get; set; }
    }
}
