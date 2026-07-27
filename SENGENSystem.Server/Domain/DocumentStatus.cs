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
        XeroxCopy = 3,

        /// <summary>
        /// A Certificate of Grades stands in for the paper while the original is still with the
        /// previous school. Offered instead of <see cref="XeroxCopy"/> on requirements flagged
        /// <see cref="AdmissionRequirement.AcceptsCertificateOfGrades"/> — the Official Transcript
        /// of Records, which a photocopy is never accepted for.
        /// </summary>
        CertificateOfGrades = 4
    }
}
