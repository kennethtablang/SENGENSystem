namespace SENGENSystem.Server.Domain
{
    /// <summary>The level of the last school a registrant attended (SIS item 15).</summary>
    public enum LastSchoolLevel
    {
        HighSchool = 1,
        JuniorHighSchool = 2,
        SeniorHighSchool = 3,

        /// <summary>Alternative Learning System — Accreditation &amp; Equivalency / PEPT.</summary>
        AlsAePept = 4,
        College = 5
    }
}
