namespace SENGENSystem.Server.Features.Scheduling.Board
{
    /// <summary>
    /// A meeting that still needs a slot on the calendar: one component (lecture or laboratory)
    /// of one <c>FacultyLoadAssignment</c> (faculty × subject × class section) for the semester.
    /// A lecture-laboratory subject therefore yields <b>two</b> pool items — its lecture hours and
    /// its laboratory hours are dragged out separately, into different kinds of room.
    /// An item disappears once its own hours are fully plotted.
    /// </summary>
    public record PoolItemDto(
        // "{facultyLoadAssignmentId}:{component}" — stable identity for a draggable item.
        string Key,
        Guid FacultyLoadAssignmentId,
        string Component,             // "Lecture" | "Laboratory"
        string ComponentLabel,
        Guid SubjectId,
        string SubjectCode,
        string SubjectTitle,
        int Units,
        string Delivery,
        string DeliveryShort,         // "LEC" | "LAB" | "LEC-LAB"
        int RequiredHours,            // hours of *this* component
        double PlottedHours,
        double RemainingHours,
        string RequiredRoomKind,      // the only room kind this meeting may occupy
        string RequiredRoomKindLabel,
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
    /// <see cref="Component"/> is read off the room's kind, which the room-suitability hard
    /// constraint keeps exact.
    /// </summary>
    public record BoardEntryDto(
        Guid AssignmentId,
        Guid RoomId,
        string RoomName,
        string RoomKind,
        string Component,
        int Day,            // DayOfWeek as int (Monday = 1 … Friday = 5)
        int StartMinutes,
        int EndMinutes,
        Guid SubjectId,
        string SubjectCode,
        string SubjectTitle,
        int Units,
        string DeliveryShort,
        bool RequiresLaboratory,
        Guid FacultyProfileId,
        string FacultyName,
        string CohortKey,
        string CohortLabel,
        bool IsPublished,
        bool IsManualOverride,
        // Changed after it was published (FR-PUB-04) — the board marks these so the Academic Head
        // can see at a glance which classes moved on people who had already been told.
        bool IsAmended,
        string? AmendedAtUtc);

    /// <summary>
    /// One line in the Weekly Hours Tracker: a single component of a subject taught by a faculty
    /// member to a class section, with the weekly contact hours that component requires. A
    /// lecture-laboratory subject contributes two lines, so the lecture hours and the laboratory
    /// hours are tracked — and can fall short — independently. The hours actually plotted are
    /// computed on the client from the live entries, so "plotted / required" (e.g. 1/3 hrs)
    /// updates as blocks are dragged, resized, and removed.
    /// </summary>
    public record SubjectHoursDto(
        string Key,
        Guid FacultyLoadAssignmentId,
        Guid FacultyProfileId,
        Guid SubjectId,
        string SubjectCode,
        string SubjectTitle,
        string Component,
        string ComponentLabel,
        string DeliveryShort,
        string RequiredRoomKind,
        string RequiredRoomKindLabel,
        string FacultyName,
        string CohortLabel,
        string CohortKey,
        int RequiredHours);

    public record BoardRoomDto(Guid Id, string Name, int Capacity, string Kind, string KindLabel, bool IsLaboratory);
    public record BoardSemesterDto(Guid Id, string Name, bool IsActive, bool IsArchived);
    public record BoardFacultyDto(Guid Id, string Name);

    /// <summary>Place a pool item (one component of an allocated subject) onto the calendar.</summary>
    public record PlaceRequest(
        Guid FacultyLoadAssignmentId,
        string? Component,
        Guid RoomId,
        int Day,
        int StartMinutes,
        int EndMinutes);

    /// <summary>Move/resize/re-room an existing placement.</summary>
    public record MoveRequest(Guid RoomId, int Day, int StartMinutes, int EndMinutes);
}
