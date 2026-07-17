namespace SENGENSystem.Server.Features.Scheduling.Board
{
    /// <summary>
    /// A subject that still needs a slot on the calendar: one <c>FacultyLoadAssignment</c>
    /// (faculty × subject × class section) for the semester that has not been placed yet.
    /// These populate the "Assigned Subjects" list the user drags onto the timetable.
    /// </summary>
    public record PoolItemDto(
        Guid FacultyLoadAssignmentId,
        Guid SubjectId,
        string SubjectCode,
        string SubjectTitle,
        int Units,
        bool RequiresLaboratory,
        Guid FacultyProfileId,
        string FacultyName,
        string ProgramCode,
        int YearLevel,
        string SectionName,
        string CohortLabel,
        string CohortKey);

    /// <summary>
    /// A placed meeting on the board — a persisted <c>ScheduleAssignment</c>, rendered as a
    /// FullCalendar event (day + minute range) tagged with its room, faculty, and cohort.
    /// </summary>
    public record BoardEntryDto(
        Guid AssignmentId,
        Guid RoomId,
        string RoomName,
        int Day,            // DayOfWeek as int (Monday = 1 … Friday = 5)
        int StartMinutes,
        int EndMinutes,
        Guid SubjectId,
        string SubjectCode,
        string SubjectTitle,
        int Units,
        bool RequiresLaboratory,
        Guid FacultyProfileId,
        string FacultyName,
        string CohortKey,
        string CohortLabel,
        bool IsPublished,
        bool IsManualOverride);

    /// <summary>
    /// One line in the Weekly Hours Tracker: a subject taught by a faculty member to a class
    /// section (i.e. one <c>FacultyLoadAssignment</c>), with the weekly contact hours it requires.
    /// The hours actually plotted on the calendar are computed on the client from the live entries,
    /// so the "plotted / required" reading (e.g. 1/3 hrs) updates as blocks are dragged/resized.
    /// </summary>
    public record SubjectHoursDto(
        Guid FacultyLoadAssignmentId,
        Guid FacultyProfileId,
        Guid SubjectId,
        string SubjectCode,
        string SubjectTitle,
        string FacultyName,
        string CohortLabel,
        string CohortKey,
        int RequiredHours);

    public record BoardRoomDto(Guid Id, string Name, int Capacity, bool IsLaboratory);
    public record BoardSemesterDto(Guid Id, string Name, bool IsActive, bool IsArchived);
    public record BoardFacultyDto(Guid Id, string Name);

    /// <summary>Place a pool item onto the calendar at a room/day/time.</summary>
    public record PlaceRequest(Guid FacultyLoadAssignmentId, Guid RoomId, int Day, int StartMinutes, int EndMinutes);

    /// <summary>Move/resize/re-room an existing placement.</summary>
    public record MoveRequest(Guid RoomId, int Day, int StartMinutes, int EndMinutes);
}
