namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// The program / track a student is registering into (SIS "CHOSEN PROGRAM / TRACK &amp;
    /// STRAND / SPECIALIZATION"). Persisted as a string, so names are the stable contract.
    /// </summary>
    public enum ProgramTrack
    {
        /// <summary>Information Technology Program.</summary>
        ITP = 1,

        /// <summary>Hospitality and Restaurant Services.</summary>
        HRS = 2,

        /// <summary>Hotel and Restaurant Administration.</summary>
        HRA = 3
    }
}
