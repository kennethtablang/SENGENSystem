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

        public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>The Admission Officer who validated (or rejected) the request.</summary>
        public Guid? ValidatedByUserId { get; set; }

        public DateTime? ValidatedAtUtc { get; set; }

        /// <summary>Optional note captured on validation/rejection.</summary>
        public string? Remarks { get; set; }
    }
}
