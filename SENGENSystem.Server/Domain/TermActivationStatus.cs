namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// Lifecycle of a returning student's <see cref="TermActivation"/> request: self-submitted
    /// and awaiting the Admission Officer's validation, validated, or rejected.
    /// </summary>
    public enum TermActivationStatus
    {
        Pending = 1,
        Validated = 2,
        Rejected = 3
    }
}
