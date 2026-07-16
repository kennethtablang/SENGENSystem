using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.FacultyLoad
{
    /// <summary>A faculty member's teaching load for a semester, on the load-management list (FR-FAC-01).</summary>
    public record FacultyLoadDto(
        Guid FacultyProfileId,
        string Name,
        string ProgramCode,
        int MaxLoadUnits,
        int AssignedUnits,
        int AssignedCount);

    /// <summary>
    /// One assignable row in the Assign Load modal: a subject taught to a specific class section
    /// (student block). A (subject, class section) pair is exclusive to one faculty per semester —
    /// when held by another member, <see cref="AssignedToProfileId"/>/<see cref="AssignedToName"/>
    /// identify them so the row is shown as taken and its checkbox muted (FR-FAC-01).
    /// </summary>
    public record LoadRowDto(
        Guid SubjectId,
        Guid ClassSectionId,
        string Code,
        string Title,
        string Course,
        int YearLevel,
        string Section,
        int Units,
        string Type,
        bool IsAssigned,
        Guid? AssignedToProfileId,
        string? AssignedToName)
    {
        public static LoadRowDto From(Subject s, ClassSection c, bool isAssigned, Guid? assignedToProfileId, string? assignedToName) =>
            new(s.Id, c.Id, s.Code, s.Title, s.ProgramCode, c.YearLevel, c.SectionName, s.Units,
                s.RequiresLaboratory ? "Laboratory" : "Lecture", isAssigned, assignedToProfileId, assignedToName);
    }

    /// <summary>Modal payload: the faculty being edited plus every assignable subject×class-section row.</summary>
    public record FacultyAssignmentsDto(
        Guid FacultyProfileId,
        string Name,
        string ProgramCode,
        int MaxLoadUnits,
        int AssignedUnits,
        IReadOnlyList<LoadRowDto> Rows);
}
