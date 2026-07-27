using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Scheduling.Board
{
    // Vertical slice: the Academic Head (and School Admin) build class schedules by hand on a
    // calendar — dragging faculty-allocated subjects (FacultyLoadAssignment) onto day/time slots
    // per room, with live conflict detection (FR-SCHED-02/05, FR-FAC-02 manual override).
    //
    // Placements are persisted as ScheduleAssignments so they share the review/publish pipeline.
    // Because the board allows free-form (30-min-snapped) times, a matching TimeSlot is created
    // on demand, and a subject-for-cohort Section is created on demand — no schema change.
    public static class ScheduleBoardEndpoints
    {
        public static IEndpointRouteBuilder MapScheduleBoard(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/scheduling/board")
                .RequireAuthorization(policy =>
                    policy.RequireRole(nameof(UserRole.AcademicHead), nameof(UserRole.SchoolAdmin)));

            group.MapGet("", GetBoardAsync);
            group.MapPost("", PlaceAsync);
            group.MapPut("/{assignmentId:guid}", MoveAsync);
            group.MapDelete("/{assignmentId:guid}", RemoveAsync);
            return app;
        }

        // GET /api/scheduling/board?semesterId= — everything the board needs in one round trip:
        // the semester selector, rooms, faculty, the unplaced "Assigned Subjects" pool, the placed
        // entries, and the per-faculty unit tracker.
        private static async Task<IResult> GetBoardAsync(Guid? semesterId, AppDbContext db, CancellationToken ct)
        {
            var semesters = await db.Semesters.AsNoTracking()
                .OrderByDescending(s => s.StartDate)
                .Select(s => new BoardSemesterDto(s.Id, s.Name, s.IsActive, s.IsArchived))
                .ToListAsync(ct);

            // Materialised before projecting: Room.IsLaboratory is derived from Kind, not a column.
            var rooms = (await db.Rooms.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct))
                .Select(r => new BoardRoomDto(r.Id, r.Name, r.Capacity, r.Kind.ToString(), r.Kind.Label(), r.IsLaboratory))
                .ToList();

            // The teaching day's opening time governs generation; the board shows the same window so
            // a hand-built timetable and a generated one are read against one grid (FR-SCHED-05).
            var classStartMinutes = (await db.GetSettingsAsync(ct)).ClassDayStartMinutes;

            var semester = await ResolveSemesterAsync(semesterId, db, ct);
            if (semester is null)
            {
                return Results.Ok(new
                {
                    semesterId = (Guid?)null,
                    semesterName = (string?)null,
                    semesters,
                    rooms,
                    classStartMinutes,
                    faculty = Array.Empty<BoardFacultyDto>(),
                    pool = Array.Empty<PoolItemDto>(),
                    entries = Array.Empty<BoardEntryDto>(),
                    hoursTracker = Array.Empty<SubjectHoursDto>()
                });
            }

            var facultyProfiles = await db.FacultyProfiles.AsNoTracking()
                .Include(f => f.User)
                .OrderBy(f => f.User!.LastName).ThenBy(f => f.User!.FirstName)
                .ToListAsync(ct);

            var loads = await db.FacultyLoadAssignments.AsNoTracking()
                .Where(l => l.SemesterId == semester.Id)
                .Include(l => l.Subject)
                .Include(l => l.ClassSection)
                .Include(l => l.FacultyProfile).ThenInclude(f => f!.User)
                .ToListAsync(ct);

            var entries = await db.ScheduleAssignments.AsNoTracking()
                .Where(a => a.SemesterId == semester.Id)
                .Include(a => a.Section).ThenInclude(s => s!.Subject)
                .Include(a => a.Room)
                .Include(a => a.TimeSlot)
                .Include(a => a.FacultyProfile).ThenInclude(f => f!.User)
                .ToListAsync(ct);

            // Minutes already plotted per (faculty, subject, cohort, component). A component is
            // "done" when its own hours are covered — a lecture-laboratory subject can be half
            // plotted, which is exactly what the tracker and the pool need to show.
            var plotted = PlottedMinutes(entries);

            // The load rows expanded into meetings: one per delivery component of the subject.
            var meetings = loads
                .Where(l => l.Subject is not null && l.ClassSection is not null)
                .OrderBy(l => l.FacultyProfile?.User?.LastName)
                .ThenBy(l => l.ClassSection!.ProgramCode).ThenBy(l => l.ClassSection!.YearLevel)
                .ThenBy(l => l.ClassSection!.SectionName).ThenBy(l => l.Subject!.Code)
                .SelectMany(l => l.Subject!.Components().Select(c => (Load: l, Component: c)))
                .ToList();

            var pool = meetings
                .Select(m =>
                {
                    var subject = m.Load.Subject!;
                    var cohort = m.Load.ClassSection!;
                    var cohortKey = CohortKey(cohort);
                    var required = subject.HoursFor(m.Component);
                    var done = plotted.GetValueOrDefault(
                        (m.Load.FacultyProfileId, m.Load.SubjectId, cohortKey, m.Component)) / 60.0;
                    var kind = subject.RoomKindFor(m.Component);
                    return new PoolItemDto(
                        PoolKey(m.Load.Id, m.Component),
                        m.Load.Id,
                        m.Component.ToString(),
                        m.Component.ToString(),
                        m.Load.SubjectId,
                        subject.Code,
                        subject.Title,
                        subject.Units,
                        subject.Delivery.ToString(),
                        subject.Delivery.ShortLabel(),
                        required,
                        Math.Round(done, 2),
                        Math.Round(Math.Max(0, required - done), 2),
                        kind.ToString(),
                        kind.Label(),
                        kind.IsLaboratory(),
                        m.Load.FacultyProfileId,
                        m.Load.FacultyProfile?.User?.FullName ?? "(unknown)",
                        cohort.ProgramCode,
                        cohort.YearLevel,
                        cohort.SectionName,
                        cohort.DisplayName,
                        cohortKey);
                })
                // Still draggable while any of its own hours are unplotted; a fully covered
                // meeting leaves the pool.
                .Where(p => p.RemainingHours > 0)
                .ToList();

            var entryDtos = entries
                .Where(a => a.Section is not null && a.TimeSlot is not null)
                .Select(ToEntryDto)
                .ToList();

            // Weekly Hours Tracker: one row per meeting (faculty × subject × class section ×
            // component) with the weekly hours that component requires. Plotted hours are summed
            // on the client from the live entries so the reading updates on every drag.
            var hoursTracker = meetings
                .Select(m =>
                {
                    var subject = m.Load.Subject!;
                    var cohort = m.Load.ClassSection!;
                    var kind = subject.RoomKindFor(m.Component);
                    return new SubjectHoursDto(
                        PoolKey(m.Load.Id, m.Component),
                        m.Load.Id,
                        m.Load.FacultyProfileId,
                        m.Load.SubjectId,
                        subject.Code,
                        subject.Title,
                        m.Component.ToString(),
                        m.Component.ToString(),
                        subject.Delivery.ShortLabel(),
                        kind.ToString(),
                        kind.Label(),
                        m.Load.FacultyProfile?.User?.FullName ?? "(unknown)",
                        cohort.DisplayName,
                        CohortKey(cohort),
                        subject.HoursFor(m.Component));
                })
                .ToList();

            var faculty = facultyProfiles.Select(f => new BoardFacultyDto(f.Id, f.User?.FullName ?? "(unknown)")).ToList();

            return Results.Ok(new
            {
                semesterId = semester.Id,
                semesterName = semester.Name,
                semesters,
                rooms,
                classStartMinutes,
                faculty,
                pool,
                entries = entryDtos,
                hoursTracker
            });
        }

        // POST /api/scheduling/board — place a pool item at a room/day/time.
        private static async Task<IResult> PlaceAsync(PlaceRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            if (Validate(request.Day, request.StartMinutes, request.EndMinutes) is { } badTime)
            {
                return badTime;
            }

            var load = await db.FacultyLoadAssignments
                .Include(l => l.Subject)
                .Include(l => l.ClassSection)
                .Include(l => l.FacultyProfile).ThenInclude(f => f!.User)
                .FirstOrDefaultAsync(l => l.Id == request.FacultyLoadAssignmentId, ct);
            if (load?.Subject is null || load.ClassSection is null)
            {
                return Results.NotFound(new { message = "That assigned subject no longer exists. Refresh the board." });
            }

            if (await EnsureNotArchivedAsync(db, load.SemesterId, ct) is { } frozen) return frozen;
            if (await EnsureNotFinalizedAsync(db, load.SemesterId, ct) is { } locked) return locked;

            var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == request.RoomId, ct);
            if (room is null) return Results.BadRequest(new { message = "The selected room no longer exists." });

            var subject = load.Subject;
            if (!Enum.TryParse<ClassComponent>(request.Component, ignoreCase: true, out var component)
                || !subject.Components().Contains(component))
            {
                return Results.BadRequest(new
                {
                    message = $"{subject.Code} is {subject.Delivery.Label().ToLowerInvariant()} — " +
                        "it has no such meeting to place. Refresh the board."
                });
            }

            // H3b, enforced by hand exactly as the engine enforces it in a generated schedule:
            // laboratory hours only in the laboratory the subject requires, lecture hours only in
            // a lecture room. Without this the single Computer Lab could be spent on lectures.
            var requiredKind = subject.RoomKindFor(component);
            if (room.Kind != requiredKind)
            {
                return Results.Conflict(new
                {
                    message = $"{subject.Code}’s {component.ToString().ToLowerInvariant()} hours need a " +
                        $"{requiredKind.Label().ToLowerInvariant()}, and “{room.Name}” is a " +
                        $"{room.Kind.Label().ToLowerInvariant()}."
                });
            }

            var cohort = load.ClassSection;
            var cohortKey = CohortKey(cohort);

            // The pool hides a meeting once its own hours are covered; re-check here so a stale
            // board (or a double POST) cannot over-plot it.
            var existing = await db.ScheduleAssignments.AsNoTracking()
                .Where(a => a.SemesterId == load.SemesterId)
                .Include(a => a.Section)
                .Include(a => a.Room)
                .Include(a => a.TimeSlot)
                .ToListAsync(ct);
            var alreadyPlottedMinutes = PlottedMinutes(existing)
                .GetValueOrDefault((load.FacultyProfileId, load.SubjectId, cohortKey, component));
            var requiredMinutes = subject.HoursFor(component) * 60;
            if (alreadyPlottedMinutes >= requiredMinutes)
            {
                return Results.Conflict(new
                {
                    message = $"{subject.Code}’s {component.ToString().ToLowerInvariant()} hours for " +
                        $"{cohort.DisplayName} are already fully plotted."
                });
            }

            var conflict = await CheckConflictAsync(
                db, load.SemesterId, null, request.RoomId, room.Name,
                load.FacultyProfileId, load.FacultyProfile?.User?.FullName ?? "This faculty member",
                cohortKey, cohort.DisplayName,
                request.Day, request.StartMinutes, request.EndMinutes, ct);
            if (conflict is not null) return conflict;

            var section = await FindOrCreateSectionAsync(db, load, cohort, ct);
            var timeSlot = await FindOrCreateTimeSlotAsync(db, request.Day, request.StartMinutes, request.EndMinutes, ct);

            var assignment = new ScheduleAssignment
            {
                SemesterId = load.SemesterId,
                SectionId = section.Id,
                RoomId = room.Id,
                TimeSlotId = timeSlot.Id,
                FacultyProfileId = load.FacultyProfileId,
                IsManualOverride = true,
                IsPublished = false
            };
            db.ScheduleAssignments.Add(assignment);

            audit.Record(AuditAction.ScheduleOverridden,
                $"Placed {load.Subject.Code} {component.ToString().ToLowerInvariant()} ({cohort.DisplayName}) " +
                $"with {load.FacultyProfile?.User?.FullName} in {room.Name}, " +
                $"{DayName(request.Day)} {Hhmm(request.StartMinutes)}–{Hhmm(request.EndMinutes)}.",
                "ScheduleAssignment", assignment.Id.ToString());
            await db.SaveChangesAsync(ct);

            // Hydrate navigations for the response DTO without a re-query.
            assignment.Section = section;
            section.Subject = load.Subject;
            assignment.Room = room;
            assignment.TimeSlot = timeSlot;
            assignment.FacultyProfile = load.FacultyProfile;
            return Results.Ok(ToEntryDto(assignment));
        }

        // PUT /api/scheduling/board/{id} — move, resize, or re-room an existing placement.
        // Moving a *published* class is allowed but is an amendment, not a quiet edit: the people
        // already told the old time are told the new one (FR-PUB-04, ScheduleAmendments).
        private static async Task<IResult> MoveAsync(
            Guid assignmentId,
            MoveRequest request,
            AppDbContext db,
            AuditLog audit,
            Notifier notifier,
            IEmailSender email,
            System.Security.Claims.ClaimsPrincipal principal,
            CancellationToken ct)
        {
            if (Validate(request.Day, request.StartMinutes, request.EndMinutes) is { } badTime)
            {
                return badTime;
            }

            var assignment = await db.ScheduleAssignments
                .Include(a => a.Section).ThenInclude(s => s!.Subject)
                .Include(a => a.Room)
                .Include(a => a.TimeSlot)
                .Include(a => a.FacultyProfile).ThenInclude(f => f!.User)
                .FirstOrDefaultAsync(a => a.Id == assignmentId, ct);
            if (assignment?.Section is null)
            {
                return Results.NotFound(new { message = "That placement no longer exists. Refresh the board." });
            }

            // Captured before the edit so the amendment notice can say what it moved *from*.
            var wasPublished = assignment.IsPublished;
            var before = assignment.TimeSlot is null
                ? null
                : ScheduleAmendments.Describe(
                    assignment.TimeSlot.Day, assignment.TimeSlot.StartMinutes,
                    assignment.TimeSlot.EndMinutes, assignment.Room?.Name ?? "its room");

            if (await EnsureNotArchivedAsync(db, assignment.SemesterId, ct) is { } frozen) return frozen;
            if (await EnsureNotFinalizedAsync(db, assignment.SemesterId, ct) is { } locked) return locked;

            var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == request.RoomId, ct);
            if (room is null) return Results.BadRequest(new { message = "The selected room no longer exists." });

            // Which meeting this is comes from the room it currently occupies, so re-rooming it
            // into a different kind would silently reclassify lecture hours as laboratory hours.
            // Same H3b rule as placement: the target room must suit this meeting.
            var movingComponent = ScheduleRowDto.ComponentOf(assignment);
            var movingSubject = assignment.Section.Subject;
            if (movingSubject is not null && room.Kind != movingSubject.RoomKindFor(movingComponent))
            {
                return Results.Conflict(new
                {
                    message = $"This is {movingSubject.Code}’s {movingComponent.ToString().ToLowerInvariant()} meeting — " +
                        $"it needs a {movingSubject.RoomKindFor(movingComponent).Label().ToLowerInvariant()}, " +
                        $"and “{room.Name}” is a {room.Kind.Label().ToLowerInvariant()}. " +
                        "Remove it and place it again to change which meeting it is."
                });
            }

            var conflict = await CheckConflictAsync(
                db, assignment.SemesterId, assignment.Id, request.RoomId, room.Name,
                assignment.FacultyProfileId, assignment.FacultyProfile?.User?.FullName ?? "This faculty member",
                assignment.Section.CohortKey, CohortLabel(assignment.Section),
                request.Day, request.StartMinutes, request.EndMinutes, ct);
            if (conflict is not null) return conflict;

            var timeSlot = await FindOrCreateTimeSlotAsync(db, request.Day, request.StartMinutes, request.EndMinutes, ct);
            assignment.RoomId = room.Id;
            assignment.TimeSlotId = timeSlot.Id;
            assignment.IsManualOverride = true;

            audit.Record(AuditAction.ScheduleOverridden,
                $"Moved {assignment.Section.Subject?.Code} ({CohortLabel(assignment.Section)}) to {room.Name}, {DayName(request.Day)} {Hhmm(request.StartMinutes)}–{Hhmm(request.EndMinutes)}.",
                "ScheduleAssignment", assignment.Id.ToString());

            var after = ScheduleAmendments.Describe(
                (DayOfWeek)request.Day, request.StartMinutes, request.EndMinutes, room.Name);
            var change = before is null ? $"was moved to {after}" : $"moved from {before} to {after}";
            if (wasPublished)
            {
                await ScheduleAmendments.RecordAsync(assignment, change, db, audit, notifier, principal, ct);
            }

            await db.SaveChangesAsync(ct);

            assignment.Room = room;
            assignment.TimeSlot = timeSlot;

            // After the commit: the edit stands whatever the mail server does.
            var emailsSent = wasPublished
                ? await ScheduleAmendments.EmailAsync(assignment, change, db, email, ct)
                : 0;

            return Results.Ok(new
            {
                entry = ToEntryDto(assignment),
                amended = wasPublished,
                change = wasPublished ? change : null,
                notifiedCount = emailsSent
            });
        }

        // DELETE /api/scheduling/board/{id} — unplace (returns the subject to the pool). Removing a
        // *published* class is the sharpest amendment of all: the people holding it need to know it
        // is gone, so the notice goes out before the row does (FR-PUB-04).
        private static async Task<IResult> RemoveAsync(
            Guid assignmentId,
            AppDbContext db,
            AuditLog audit,
            Notifier notifier,
            IEmailSender email,
            System.Security.Claims.ClaimsPrincipal principal,
            CancellationToken ct)
        {
            var assignment = await db.ScheduleAssignments
                .Include(a => a.Section).ThenInclude(s => s!.Subject)
                .Include(a => a.Room)
                .Include(a => a.TimeSlot)
                .FirstOrDefaultAsync(a => a.Id == assignmentId, ct);
            if (assignment is null) return Results.NotFound(new { message = "That placement no longer exists." });

            if (await EnsureNotArchivedAsync(db, assignment.SemesterId, ct) is { } frozen) return frozen;
            if (await EnsureNotFinalizedAsync(db, assignment.SemesterId, ct) is { } locked) return locked;

            var wasPublished = assignment.IsPublished;
            var where = assignment.TimeSlot is null
                ? "its published slot"
                : ScheduleAmendments.Describe(
                    assignment.TimeSlot.Day, assignment.TimeSlot.StartMinutes,
                    assignment.TimeSlot.EndMinutes, assignment.Room?.Name ?? "its room");
            var change = $"was removed from the schedule (it was {where})";

            // Notices are staged against the still-present row, then the row is removed in the same
            // transaction — the notice and the removal commit together or not at all.
            if (wasPublished)
            {
                await ScheduleAmendments.RecordAsync(assignment, change, db, audit, notifier, principal, ct);
            }

            audit.Record(AuditAction.ScheduleOverridden,
                $"Removed {assignment.Section?.Subject?.Code} from the calendar.",
                "ScheduleAssignment", assignment.Id.ToString());

            db.ScheduleAssignments.Remove(assignment);
            await db.SaveChangesAsync(ct);

            // After the commit, as with a move: the removal stands whatever the mail server does.
            // The recipients are looked up by section and faculty id, both of which outlive the row.
            var emailsSent = wasPublished
                ? await ScheduleAmendments.EmailAsync(assignment, change, db, email, ct)
                : 0;

            return Results.Ok(new { removed = true, amended = wasPublished, notifiedCount = emailsSent });
        }

        // ---- helpers ----

        // Archived semesters are frozen history (FR: schedule archiving) — every write refuses.
        private static async Task<IResult?> EnsureNotArchivedAsync(AppDbContext db, Guid semesterId, CancellationToken ct)
        {
            var archived = await db.Semesters.AsNoTracking()
                .Where(s => s.Id == semesterId)
                .Select(s => s.IsArchived)
                .FirstOrDefaultAsync(ct);
            return archived
                ? Results.Conflict(new { message = "This semester is archived — its schedule is read-only." })
                : null;
        }

        // A finalized draft is locked (FR-SCHED-06) — board edits refuse until it is reopened.
        private static async Task<IResult?> EnsureNotFinalizedAsync(AppDbContext db, Guid semesterId, CancellationToken ct)
        {
            var finalized = await db.ScheduleAssignments.AsNoTracking()
                .AnyAsync(a => a.SemesterId == semesterId && a.IsFinalized && !a.IsPublished, ct);
            return finalized
                ? Results.Conflict(new { message = "This schedule is finalized and locked. Reopen it on the Generate page to make changes." })
                : null;
        }

        private static IResult? Validate(int day, int start, int end)
        {
            if (day is < 1 or > 6)
                return Results.BadRequest(new { message = "Classes can only be scheduled Monday to Saturday." });
            if (start < 0 || end > 24 * 60 || start >= end)
                return Results.BadRequest(new { message = "That time range is invalid." });
            return null;
        }

        // Returns a 409 IResult describing the first hard-constraint clash, or null when the slot is free.
        private static async Task<IResult?> CheckConflictAsync(
            AppDbContext db, Guid semesterId, Guid? excludeId,
            Guid roomId, string roomName, Guid facultyProfileId, string facultyName,
            string cohortKey, string cohortLabel, int day, int start, int end, CancellationToken ct)
        {
            var others = await db.ScheduleAssignments.AsNoTracking()
                .Where(a => a.SemesterId == semesterId && a.Id != (excludeId ?? Guid.Empty))
                .Include(a => a.Section).ThenInclude(s => s!.Subject)
                .Include(a => a.TimeSlot)
                .Where(a => a.TimeSlot!.Day == (DayOfWeek)day)
                .ToListAsync(ct);

            foreach (var o in others)
            {
                if (o.TimeSlot is null || o.Section is null) continue;
                // Overlap on the same day: start < otherEnd && otherStart < end.
                if (!(start < o.TimeSlot.EndMinutes && o.TimeSlot.StartMinutes < end)) continue;

                var when = $"{DayName(day)} {Hhmm(o.TimeSlot.StartMinutes)}–{Hhmm(o.TimeSlot.EndMinutes)}";
                if (o.RoomId == roomId)
                    return Results.Conflict(new { message = $"{roomName} is already booked {when} ({CohortLabel(o.Section)} · {o.Section.Subject?.Code})." });
                if (o.FacultyProfileId == facultyProfileId)
                    return Results.Conflict(new { message = $"{facultyName} is already teaching {when} ({o.Section.Subject?.Code})." });
                if (o.Section.CohortKey == cohortKey)
                    return Results.Conflict(new { message = $"{cohortLabel} already has a class {when} ({o.Section.Subject?.Code})." });
            }
            return null;
        }

        private static async Task<Section> FindOrCreateSectionAsync(
            AppDbContext db, FacultyLoadAssignment load, ClassSection cohort, CancellationToken ct)
        {
            var section = await db.Sections.FirstOrDefaultAsync(s =>
                s.SemesterId == load.SemesterId && s.SubjectId == load.SubjectId
                && s.ProgramCode == cohort.ProgramCode && s.YearLevel == cohort.YearLevel
                && s.Block == cohort.SectionName, ct);
            if (section is not null) return section;

            section = new Section
            {
                SubjectId = load.SubjectId,
                SemesterId = load.SemesterId,
                SectionCode = $"{cohort.ProgramCode}-{cohort.YearLevel}{cohort.SectionName}-{load.Subject!.Code}",
                ProgramCode = cohort.ProgramCode,
                YearLevel = cohort.YearLevel,
                Block = cohort.SectionName,
                Capacity = await db.GetSectionCapacityCapAsync(ct)
            };
            db.Sections.Add(section);
            return section;
        }

        private static async Task<TimeSlot> FindOrCreateTimeSlotAsync(
            AppDbContext db, int day, int start, int end, CancellationToken ct)
        {
            var slot = await db.TimeSlots.FirstOrDefaultAsync(t =>
                t.Day == (DayOfWeek)day && t.StartMinutes == start && t.EndMinutes == end, ct);
            if (slot is not null) return slot;

            // A period persisting one manual placement — reuse an allowable grid slot when the drop
            // lands exactly on one (found above), otherwise this synthetic period isn't grid config.
            slot = new TimeSlot { Day = (DayOfWeek)day, StartMinutes = start, EndMinutes = end, IsAllowable = false };
            db.TimeSlots.Add(slot);
            return slot;
        }

        private static BoardEntryDto ToEntryDto(ScheduleAssignment a)
        {
            var kind = a.Room?.Kind ?? Domain.RoomKind.LectureRoom;
            return new BoardEntryDto(
                a.Id,
                a.RoomId,
                a.Room?.Name ?? string.Empty,
                kind.ToString(),
                ScheduleRowDto.ComponentOf(a).ToString(),
                (int)(a.TimeSlot?.Day ?? DayOfWeek.Monday),
                a.TimeSlot?.StartMinutes ?? 0,
                a.TimeSlot?.EndMinutes ?? 0,
                a.Section?.SubjectId ?? Guid.Empty,
                a.Section?.Subject?.Code ?? string.Empty,
                a.Section?.Subject?.Title ?? string.Empty,
                a.Section?.Subject?.Units ?? 0,
                (a.Section?.Subject?.Delivery ?? SubjectDelivery.LectureOnly).ShortLabel(),
                a.Section?.Subject?.RequiresLaboratory ?? false,
                a.FacultyProfileId,
                a.FacultyProfile?.User?.FullName ?? string.Empty,
                a.Section?.CohortKey ?? string.Empty,
                a.Section is null ? string.Empty : CohortLabel(a.Section),
                a.IsPublished,
                a.IsManualOverride,
                a.IsAmended,
                a.AmendedAtUtc is { } amended
                    ? DateTime.SpecifyKind(amended, DateTimeKind.Utc).ToString("o")
                    : null);
        }

        /// <summary>
        /// Minutes already on the calendar per (faculty, subject, cohort, component). The
        /// component is read off each placement's room, which the room-kind constraint keeps
        /// exact — so a lecture-laboratory subject's two halves are counted separately.
        /// </summary>
        private static Dictionary<(Guid, Guid, string, ClassComponent), int> PlottedMinutes(
            IEnumerable<ScheduleAssignment> assignments) =>
            assignments
                .Where(a => a.Section is not null && a.TimeSlot is not null)
                .GroupBy(a => (
                    a.FacultyProfileId,
                    a.Section!.SubjectId,
                    a.Section.CohortKey,
                    ScheduleRowDto.ComponentOf(a)))
                .ToDictionary(g => g.Key, g => g.Sum(a => a.TimeSlot!.EndMinutes - a.TimeSlot.StartMinutes));

        private static string PoolKey(Guid loadId, ClassComponent component) => $"{loadId}:{component}";

        private static string CohortKey(ClassSection c) => $"{c.ProgramCode}-{c.YearLevel}-{c.SectionName}";
        private static string CohortLabel(Section s) => $"{s.ProgramCode} {s.YearLevel}-{s.Block}";

        private static string DayName(int day) => ((DayOfWeek)day).ToString();
        private static string Hhmm(int minutes) => $"{minutes / 60:D2}:{minutes % 60:D2}";

        private static async Task<Semester?> ResolveSemesterAsync(Guid? semesterId, AppDbContext db, CancellationToken ct) =>
            semesterId is { } id
                ? await db.Semesters.FirstOrDefaultAsync(s => s.Id == id, ct)
                : await db.Semesters.FirstOrDefaultAsync(s => s.IsActive, ct)
                    ?? await db.Semesters.OrderByDescending(s => s.StartDate).FirstOrDefaultAsync(ct);
    }
}
