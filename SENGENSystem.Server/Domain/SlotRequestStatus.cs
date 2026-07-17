namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// Lifecycle of a <see cref="SlotRequest"/> (FR-ENL-04): a student requests a seat, the
    /// Registrar approves or rejects it; students may cancel their own pending requests.
    /// </summary>
    public enum SlotRequestStatus
    {
        Requested = 1,
        Approved = 2,
        Rejected = 3,
        Cancelled = 4
    }
}
