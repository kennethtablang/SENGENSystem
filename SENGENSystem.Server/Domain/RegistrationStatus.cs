namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// Lifecycle of a <see cref="StudentRegistration"/>: newly self-submitted through the public
    /// SIS, confirmed by the Registrar (FR-SIS-04), or rejected.
    /// </summary>
    public enum RegistrationStatus
    {
        Submitted = 1,
        Confirmed = 2,
        Rejected = 3
    }
}
