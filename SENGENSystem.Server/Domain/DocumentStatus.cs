namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// Submission state of a required document (SIS marks each as "Submitted" original or
    /// "Xerox Copy"). Verified and updated by school personnel (FR-DOC-02).
    /// </summary>
    public enum DocumentStatus
    {
        NotSubmitted = 1,

        /// <summary>Original document submitted.</summary>
        Submitted = 2,

        /// <summary>Photocopy submitted (original still pending).</summary>
        XeroxCopy = 3
    }
}
