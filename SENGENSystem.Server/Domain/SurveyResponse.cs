namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// One completed ISO/IEC 25010 rating-survey submission. Captures the respondent's identity and
    /// profile (name/role/email auto-filled from their account, plus the extra profile fields they
    /// confirm), the Likert answers keyed by question (stored as JSON since the question bank is a
    /// fixed code-side instrument), and the open-ended Suggestions and Further Comments.
    /// </summary>
    public class SurveyResponse
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid InvitationId { get; set; }

        public SurveyInvitation? Invitation { get; set; }

        // Respondent identity + profile (FR: collect the name and related data of the answerer).
        public string RespondentName { get; set; } = string.Empty;

        public string RespondentEmail { get; set; } = string.Empty;

        public string RespondentRole { get; set; } = string.Empty;

        public string Position { get; set; } = string.Empty;

        public int? Age { get; set; }

        public string Sex { get; set; } = string.Empty;

        public string Department { get; set; } = string.Empty;

        public string YearsUsing { get; set; } = string.Empty;

        /// <summary>Answers as a JSON object: question key → Likert score (1–5).</summary>
        public string AnswersJson { get; set; } = "{}";

        public string? Suggestions { get; set; }

        public string? FurtherComments { get; set; }

        public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
