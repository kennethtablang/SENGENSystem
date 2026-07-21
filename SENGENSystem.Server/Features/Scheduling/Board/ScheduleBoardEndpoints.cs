using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
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

            var rooms = await db.Rooms.AsNoTracking()
                .OrderBy(r => r.Name)
                .Select(r => new BoardRoomDto(r.Id, r.Name, r.Capacity, r.IsLaboratory))
                .ToListAsync(ct);

            var semester = await ResolveSemesterAsync(semesterId, db, ct);
            if (semester is null)
            {
                return Results.Ok(new
                {
                    semesterId = (Guid?)null,
                    semesterName = (string?)null,
                    semesters,
                    rooms,
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

            // A load row is "placed" once a schedule entry exists for the same faculty, subject,
            // and cohort. Keyed on that triple so pool and calendar stay in sync.
            var placedKeys = entries
                .Where(a => a.Section is not null)
                .Select(a => (a.FacultyProfileId, a.Section!.SubjectId, a.Section.CohortKey))
                .ToHashSet();

            var pool = loads
                .Where(l => l.Subject is not null && l.ClassSection is not null)
                .Where(l => !placedKeys.Contains((l.FacultyProfileId, l.SubjectId, CohortKey(l.ClassSection!))))
                .OrderBy(l => l.FacultyProfile?.User?.LastName)
                .ThenBy(l => l.ClassSection!.ProgramCode).ThenBy(l => l.ClassSection!.YearLevel)
                .ThenBy(l => l.ClassSection!.SectionName).ThenBy(l => l.Subject!.Code)
                .Select(l => new PoolItemDto(
                    l.Id,
                    l.SubjectId,
                    l.Subject!.Code,
                    l.Subject.Title,
                    l.Subject.Units,
                    l.Subject.RequiresLaboratory,
                    l.FacultyProfileId,
                    l.FacultyProfile?.User?.FullName ?? "(unknown)",
                    l.ClassSection!.ProgramCode,
                    l.ClassSection.YearLevel,
                    l.ClassSection.SectionName,
                    l.ClassSection.DisplayName,
                    CohortKey(l.ClassSection)))
                .ToList();

            var entryDtos = entries
                .Where(a => a.Section is not null && a.TimeSlot is not null)
                .Select(ToEntryDto)
                .ToList();

            // Weekly Hours Tracker: one row per assigned subject (faculty × subject × class section)
            // with the weekly hours it requires. Plotted hours are summed on the client from entries.
            var hoursTracker = loads
                .Where(l => l.Subject is not null && l.ClassSection is not null)
                .OrderBy(l => l.FacultyProfile?.User?.LastName)
                .ThenBy(l => l.ClassSection!.ProgramCode).ThenBy(l => l.ClassSection!.YearLevel)
                .ThenBy(l => l.ClassSection!.SectionName).ThenBy(l => l.Subject!.Code)
                .Select(l => new SubjectHoursDto(
                    l.Id,
                    l.FacultyProfileId,
                    l.SubjectId,
                    l.Subject!.Code,
                    l.Subject.Title,
                    l.FacultyProfile?.User?.FullName ?? "(unknown)",
                    l.ClassSection!.DisplayName,
                    CohortKey(l.ClassSection),
                    l.Subject.Hours))
                .ToList();

            var faculty = facultyProfiles.Select(f => new BoardFacultyDto(f.Id, f.User?.FullName ?? "(unknown)")).ToList();

            return Results.Ok(new
            {
                semesterId = semester.Id,
                semesterName = semester.Name,
                semesters,
                rooms,
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

            var cohort = load.ClassSection;
            var cohortKey = CohortKey(cohort);

            // One placement per (faculty, subject, cohort) — the pool already hides placed rows,
            // but guard against a double POST.
            var alreadyPlaced = await db.ScheduleAssignments
                .AnyAsync(a => a.SemesterId == load.SemesterId
                    && a.FacultyProfileId == load.FacultyProfileId
                    && a.Section!.SubjectId == load.SubjectId
                    && a.Section.ProgramCode == cohort.ProgramCode
                    && a.Section.YearLevel == cohort.YearLevel
                    && a.Section.Block == cohort.SectionName, ct);
            if (alreadyPlaced)
            {
                return Results.Conflict(new { message = $"{load.Subject.Code} for {cohort.DisplayName} is already on the calendar." });
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
                $"Placed {load.Subject.Code} ({cohort.DisplayName}) with {load.FacultyProfile?.User?.FullName} in {room.Name}, {DayName(request.Day)} {Hhmm(request.StartMinutes)}–{Hhmm(request.EndMinutes)}.",
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
        private static async Task<IResult> MoveAsync(
            Guid assignmentId, MoveRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            if (Validate(request.Day, request.StartMinutes, request.EndMinutes) is { } badTime)
            {
                return badTime;
            }

            var assignment = await db.ScheduleAssignments
                .Include(a => a.Section).ThenInclude(s => s!.Subject)
                .Include(a => a.FacultyProfile).ThenInclude(f => f!.User)
                .FirstOrDefaultAsync(a => a.Id == assignmentId, ct);
            if (assignment?.Section is null)
            {
                return Results.NotFound(new { message = "That placement no longer exists. Refresh the board." });
            }

            if (await EnsureNotArchivedAsync(db, assignment.SemesterId, ct) is { } frozen) return frozen;
            if (await EnsureNotFinalizedAsync(db, assignment.SemesterId, ct) is { } locked) return locked;

            var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == request.RoomId, ct);
            if (room is null) return Results.BadRequest(new { message = "The selected room no longer exists." });

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
            await db.SaveChangesAsync(ct);

            assignment.Room = room;
            assignment.TimeSlot = timeSlot;
            return Results.Ok(ToEntryDto(assignment));
        }

        // DELETE /api/scheduling/board/{id} — unplace (returns the subject to the pool).
        private static async Task<IResult> RemoveAsync(Guid assignmentId, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var assignment = await db.ScheduleAssignments
                .Include(a => a.Section).ThenInclude(s => s!.Subject)
                .FirstOrDefaultAsync(a => a.Id == assignmentId, ct);
            if (assignment is null) return Results.NotFound(new { message = "That placement no longer exists." });

            if (await EnsureNotArchivedAsync(db, assignment.SemesterId, ct) is { } frozen) return frozen;
            if (await EnsureNotFinalizedAsync(db, assignment.SemesterId, ct) is { } locked) return locked;

            db.ScheduleAssignments.Remove(assignment);
            audit.Record(AuditAction.ScheduleOverridden,
                $"Removed {assignment.Section?.Subject?.Code} from the calendar.",
                "ScheduleAssignment", assignment.Id.ToString());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
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

            slot = new TimeSlot { Day = (DayOfWeek)day, StartMinutes = start, EndMinutes = end };
            db.TimeSlots.Add(slot);
            return slot;
        }

        private static BoardEntryDto ToEntryDto(ScheduleAssignment a) => new(
            a.Id,
            a.RoomId,
            a.Room?.Name ?? string.Empty,
            (int)(a.TimeSlot?.Day ?? DayOfWeek.Monday),
            a.TimeSlot?.StartMinutes ?? 0,
            a.TimeSlot?.EndMinutes ?? 0,
            a.Section?.SubjectId ?? Guid.Empty,
            a.Section?.Subject?.Code ?? string.Empty,
            a.Section?.Subject?.Title ?? string.Empty,
            a.Section?.Subject?.Units ?? 0,
            a.Section?.Subject?.RequiresLaboratory ?? false,
            a.FacultyProfileId,
            a.FacultyProfile?.User?.FullName ?? string.Empty,
            a.Section?.CohortKey ?? string.Empty,
            a.Section is null ? string.Empty : CohortLabel(a.Section),
            a.IsPublished,
            a.IsManualOverride);

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
