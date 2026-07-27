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
        string RoomKind,
        // Which meeting of the subject this row is — "Lecture" or "Laboratory". A
        // lecture-laboratory subject produces one row of each, in different rooms.
        string Component,
        string Day,
        // Pre-formatted 24-hour text, kept for callers (exports, emails) that need a string.
        // Screens should format StartMinutes/EndMinutes themselves so the reader's 12/24-hour
        // Settings preference is honoured — a server-baked string can't follow a device setting.
        string Time,
        int StartMinutes,
        int EndMinutes,
        string Faculty,
        bool IsPublished,
        bool IsManualOverride,
        bool IsFinalized,
        bool IsAmended)
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
                (a.Room?.Kind ?? Domain.RoomKind.LectureRoom).ToString(),
                ComponentOf(a).ToString(),
                a.TimeSlot?.Day.ToString() ?? string.Empty,
                a.TimeSlot is null ? string.Empty : $"{Format(a.TimeSlot.StartMinutes)}–{Format(a.TimeSlot.EndMinutes)}",
                a.TimeSlot?.StartMinutes ?? 0,
                a.TimeSlot?.EndMinutes ?? 0,
                a.FacultyProfile?.User?.FullName ?? string.Empty,
                a.IsPublished,
                a.IsManualOverride,
                a.IsFinalized,
                a.IsAmended);

        private static string Format(int minutes) => $"{minutes / 60:D2}:{minutes % 60:D2}";

        /// <summary>
        /// Which component a placement is, read off the room it occupies. The room-kind hard
        /// constraint (H3b) makes this exact rather than a guess: laboratory hours are only ever
        /// in a laboratory and lecture hours only ever in a lecture room, so no separate column
        /// is needed on the assignment.
        /// </summary>
        internal static ClassComponent ComponentOf(ScheduleAssignment a) =>
            a.Room?.Kind.IsLaboratory() == true ? ClassComponent.Laboratory : ClassComponent.Lecture;
    }
}
