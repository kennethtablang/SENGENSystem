using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Documents
{
    /// <summary>
    /// Shared checklist semantics (FR-DOC): a checklist is complete when every tracked paper
    /// has been received in some form — original or photocopy (any status except NotSubmitted).
    /// </summary>
    internal static class DocumentChecklist
    {
        public static bool IsComplete(IEnumerable<RegistrationDocument> documents) =>
            documents.All(d => d.Status != DocumentStatus.NotSubmitted);

        public static int SubmittedCount(IEnumerable<RegistrationDocument> documents) =>
            documents.Count(d => d.Status != DocumentStatus.NotSubmitted);

        /// <summary>Friendly names matching the paper SIS "ADMISSION REQUIREMENTS" wording.</summary>
        public static string Label(DocumentType type) => type switch
        {
            DocumentType.Form138_SF9 => "Form 138 / SF9-SHS (Report Card)",
            DocumentType.Form137_SF10 => "Form 137 / SF10-SHS (Permanent Record)",
            DocumentType.GoodMoral => "Certificate of Good Moral Character",
            DocumentType.PsaBirthCertificate => "PSA Birth Certificate",
            DocumentType.OfficialTranscript => "Official Transcript of Records",
            DocumentType.HonorableDismissal => "Certificate of Transfer (Honorable Dismissal)",
            DocumentType.HepaA => "Hepatitis A Vaccination Record",
            DocumentType.HepaB => "Hepatitis B Vaccination Record",
            DocumentType.Xray => "Chest X-ray Result",
            _ => type.ToString()
        };
    }
}
