namespace SENGENSystem.Server.Domain
{
    /// <summary>Whether a SIS registrant is a brand-new enrollee or a transferee (SIS §1, FR-SIS-01).</summary>
    public enum StudentType
    {
        NewStudent = 1,
        Transferee = 2
    }
}
