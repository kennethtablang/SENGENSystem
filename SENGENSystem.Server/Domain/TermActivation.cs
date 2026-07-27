namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// A returning student's request to be activated for a new term without re-filing the SIS.
    /// Raised through the public self-service lookup and validated by the Admission Officer
    /// (pre-authorization of returning students).
    /// </summary>
    public class TermActivation
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid StudentRegistrationId { get; set; }

        public StudentRegistration? StudentRegistration { get; set; }

        /// <summary>The term being activated for (the active semester at request time).</summary>
        public Guid SemesterId { get; set; }

        public Semester? Semester { get; set; }

        public TermActivationStatus Status { get; set; } = TermActivationStatus.Pending;

        /// <summary>
        /// The year level the student themselves confirmed when they filed the request. Activation
        /// is a two-step flow: the student first checks the year level and term they are coming
        /// back into, then finalizes. Recording what they agreed to gives the Admission Officer the
        /// student's own answer to compare against the derived one — it is evidence, not authority:
        /// the officer's validation still settles <see cref="StudentRegistration.YearLevel"/>.
        /// Null for requests filed before the confirmation step existed.
        /// </summary>
        public int? DeclaredYearLevel { get; set; }

        public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>The Admission Officer who validated (or rejected) the request.</summary>
        public Guid? ValidatedByUserId { get; set; }

        public DateTime? ValidatedAtUtc { get; set; }

        /// <summary>Optional note captured on validation/rejection.</summary>
        public string? Remarks { get; set; }
    }
}
