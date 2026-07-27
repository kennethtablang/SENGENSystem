using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Registration.TransfereeEvaluation
{
    /// <summary>One curriculum subject on the evaluation sheet, with the Registrar's verdict.</summary>
    public record EvaluationSubjectDto(
        Guid SubjectId,
        string Code,
        string Title,
        int Units,
        int YearLevel,
        string Term,
        string TermLabel,
        string Decision,
        string? SourceSubject,
        string? SourceGrade,
        IReadOnlyList<string> Prerequisites);

    /// <summary>A row in the Registrar's evaluation queue.</summary>
    public record EvaluationQueueRowDto(
        Guid RegistrationId,
        string RegistrationNumber,
        string? OfficialStudentNumber,
        string FullName,
        string Program,
        string RegistrationStatus,
        string? SemesterName,
        string Status,
        int CreditedUnits,
        int ToTakeUnits,
        int UndecidedCount,
        int YearLevel,
        string? EvaluatedAtUtc);

    /// <summary>The full evaluation sheet for one transferee.</summary>
    public record EvaluationSheetDto(
        Guid RegistrationId,
        string RegistrationNumber,
        string? OfficialStudentNumber,
        string FullName,
        string Program,
        string StudentType,
        string RegistrationStatus,
        string? SchoolName,
        string? SchoolProgram,
        Guid? CurriculumId,
        string? CurriculumName,
        string Status,
        int CreditedUnits,
        int ToTakeUnits,
        int TotalUnits,
        int UndecidedCount,
        int RecommendedYearLevel,
        int AssignedYearLevel,
        int StudentYearLevel,
        string? Remarks,
        string? EvaluatedAtUtc,
        IReadOnlyList<EvaluationSubjectDto> Subjects);

    /// <summary>A saved decision for one subject.</summary>
    public record EvaluationItemRequest(
        Guid SubjectId, string? Decision, string? SourceSubject, string? SourceGrade);

    public record SaveEvaluationRequest(string? Remarks, IReadOnlyList<EvaluationItemRequest>? Items);

    /// <summary>
    /// Sign-off. <c>AssignedYearLevel</c> overrides the derived recommendation when the Registrar
    /// disagrees with it; omitted, the recommendation stands.
    /// </summary>
    public record CompleteEvaluationRequest(int? AssignedYearLevel, string? Remarks);

    internal static class EvaluationMapping
    {
        public static string TermLabel(SemesterTerm term) =>
            term == SemesterTerm.SecondSemester ? "2nd Semester" : "1st Semester";
    }
}
