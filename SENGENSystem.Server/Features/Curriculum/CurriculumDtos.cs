using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Curriculum
{
    /// <summary>Read models for the Academic Head's Subjects &amp; Curriculum screen (FR-SCHED-04).</summary>
    public record CurriculumDto(
        Guid Id,
        string ProgramCode,
        string ProgramName,
        int EffectivityYear,
        string Label,
        bool IsActive,
        int SubjectCount)
    {
        public static CurriculumDto From(Domain.Curriculum c, int subjectCount) =>
            new(c.Id, c.ProgramCode, c.ProgramName, c.EffectivityYear,
                $"{c.ProgramCode} {c.EffectivityYear}", c.IsActive, subjectCount);
    }

    /// <summary>A compact subject reference used for prerequisite chips and the prerequisite picker.</summary>
    public record SubjectRefDto(Guid Id, string Code, string Title)
    {
        public static SubjectRefDto From(Subject s) => new(s.Id, s.Code, s.Title);
    }

    public record SubjectDto(
        Guid Id,
        Guid? CurriculumId,
        string Code,
        string Title,
        int Units,
        int YearLevel,
        bool RequiresLaboratory,
        IReadOnlyList<SubjectRefDto> Prerequisites)
    {
        public static SubjectDto From(Subject s) =>
            new(s.Id, s.CurriculumId, s.Code, s.Title, s.Units, s.YearLevel, s.RequiresLaboratory,
                s.Prerequisites
                    .Where(p => p.PrerequisiteSubject is not null)
                    .Select(p => SubjectRefDto.From(p.PrerequisiteSubject!))
                    .OrderBy(r => r.Code)
                    .ToList());
    }
}
