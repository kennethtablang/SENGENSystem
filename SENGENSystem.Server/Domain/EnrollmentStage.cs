namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// Where a term sits in the enrollment cycle. The middle three mirror the enrollment
    /// stages SEN-GEN digitizes (document submission → registration → subject enlistment);
    /// tuition payment stays with the institution's cashiering process and is out of scope,
    /// so the cycle ends at <see cref="Closed"/>.
    ///
    /// Persisted as a string, so the names are the stable contract — append, never renumber.
    /// </summary>
    public enum EnrollmentStage
    {
        /// <summary>Back-office setup: curricula, sections, and the timetable are still being built.</summary>
        Preparation = 1,

        /// <summary>Admission requirements are being collected and verified (FR-DOC).</summary>
        DocumentSubmission = 2,

        /// <summary>Students are filing Student Information Sheets and the Registrar is confirming them (FR-SIS).</summary>
        Registration = 3,

        /// <summary>Cleared students are reserving seats in published sections (FR-ENL).</summary>
        Enlistment = 4,

        /// <summary>Enrollment for the term has ended; the roster is final.</summary>
        Closed = 5
    }
}
