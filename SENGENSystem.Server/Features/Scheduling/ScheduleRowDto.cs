using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Scheduling
{
    /// <summary>A single readable line of a generated/published schedule (shared by generate + list slices).</summary>
    public record ScheduleRowDto(
        Guid AssignmentId,
        Guid SectionId,
        string SectionCode,
        string SubjectCode,
        string SubjectTitle,
        string CohortKey,
        string Room,
        string Day,
        string Time,
        string Faculty,
        bool IsPublished,
        bool IsManualOverride)
    {
        public static ScheduleRowDto From(ScheduleAssignment a) =>
            new(
                a.Id,
                a.SectionId,
                a.Section?.SectionCode ?? string.Empty,
                a.Section?.Subject?.Code ?? string.Empty,
                a.Section?.Subject?.Title ?? string.Empty,
                a.Section?.CohortKey ?? string.Empty,
                a.Room?.Name ?? string.Empty,
                a.TimeSlot?.Day.ToString() ?? string.Empty,
                a.TimeSlot is null ? string.Empty : $"{Format(a.TimeSlot.StartMinutes)}–{Format(a.TimeSlot.EndMinutes)}",
                a.FacultyProfile?.User?.FullName ?? string.Empty,
                a.IsPublished,
                a.IsManualOverride);

        private static string Format(int minutes) => $"{minutes / 60:D2}:{minutes % 60:D2}";
    }
}
