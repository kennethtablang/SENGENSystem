using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Curriculum
{
    /// <summary>Read models for the Academic Head's Subjects &amp; Curriculum screen (FR-SCHED-04).</summary>
    public record SchoolYearRef(Guid Id, string Name);

    public record CurriculumDto(
        Guid Id,
        string ProgramCode,
        string ProgramName,
        string Label,
        bool IsActive,
        bool IsArchived,
        DateTime? ArchivedAtUtc,
        string? ArchiveReason,
        int SubjectCount,
        IReadOnlyList<SchoolYearRef> SchoolYears)
    {
        public static CurriculumDto From(Domain.Curriculum c, int subjectCount) =>
            new(c.Id, c.ProgramCode, c.ProgramName, c.ProgramCode, c.IsActive,
                c.IsArchived, c.ArchivedAtUtc, c.ArchiveReason, subjectCount,
                c.SchoolYears
                    .Where(l => l.SchoolYear is not null)
                    .OrderBy(l => l.SchoolYear!.StartDate)
                    .Select(l => new SchoolYearRef(l.SchoolYearId, l.SchoolYear!.Name))
                    .ToList());
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
        int Hours,
        int YearLevel,
        string Term,
        bool RequiresLaboratory,
        bool IsArchived,
        DateTime? ArchivedAtUtc,
        string? ArchiveReason,
        IReadOnlyList<SubjectRefDto> Prerequisites)
    {
        public static SubjectDto From(Subject s) =>
            new(s.Id, s.CurriculumId, s.Code, s.Title, s.Units, s.Hours, s.YearLevel, s.Term.ToString(), s.RequiresLaboratory,
                s.IsArchived, s.ArchivedAtUtc, s.ArchiveReason,
                s.Prerequisites
                    .Where(p => p.PrerequisiteSubject is not null)
                    .Select(p => SubjectRefDto.From(p.PrerequisiteSubject!))
                    .OrderBy(r => r.Code)
                    .ToList());
    }
}
