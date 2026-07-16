namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// The two academic terms of a school year. A concrete <see cref="Semester"/> is one of these
    /// within its school year, and a curriculum <see cref="Subject"/> is offered in one of them.
    /// Persisted as a string, so the names are the stable contract.
    /// </summary>
    public enum SemesterTerm
    {
        FirstSemester = 1,
        SecondSemester = 2
    }
}
