namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// Admission-requirement papers tracked per enrollee (SIS "ADMISSION REQUIREMENTS", FR-DOC-01).
    /// Persisted as strings; the list is intentionally extensible — append, never renumber.
    /// </summary>
    public enum DocumentType
    {
        /// <summary>Form 138 / SF9-SHS (Progress Report Card).</summary>
        Form138_SF9 = 1,

        /// <summary>Form 137 / SF10-SHS (Permanent Record).</summary>
        Form137_SF10 = 2,

        GoodMoral = 3,

        PsaBirthCertificate = 4,

        /// <summary>Official Transcript of Records (copy for STI).</summary>
        OfficialTranscript = 5,

        /// <summary>Certificate of Transfer (Honorable Dismissal).</summary>
        HonorableDismissal = 6,

        HepaA = 7,
        HepaB = 8,
        Xray = 9
    }
}
