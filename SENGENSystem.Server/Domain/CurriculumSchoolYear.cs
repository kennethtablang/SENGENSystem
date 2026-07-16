namespace SENGENSystem.Server.Domain
{
    /// <summary>
    /// Effectivity link: a <see cref="Curriculum"/> is in effect for a <see cref="SchoolYear"/>.
    /// A program's school year maps to at most one curriculum (enforced in the endpoint).
    /// </summary>
    public class CurriculumSchoolYear
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid CurriculumId { get; set; }

        public Curriculum? Curriculum { get; set; }

        public Guid SchoolYearId { get; set; }

        public SchoolYear? SchoolYear { get; set; }
    }
}
