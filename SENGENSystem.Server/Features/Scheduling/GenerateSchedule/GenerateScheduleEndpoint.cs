using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Scheduling.Engine;

namespace SENGENSystem.Server.Features.Scheduling.GenerateSchedule
{
    // Vertical slice: the Academic Head triggers CSP schedule generation for a semester
    // and reviews the result before publishing (FR-SCHED-01/06, FR-FAC-04).
    public record GenerateScheduleRequest(Guid? SemesterId);

    public record GenerateScheduleResponse(
        Guid SemesterId,
        string SemesterName,
        int SectionCount,
        int AssignedCount,
        int Steps,
        IReadOnlyList<ScheduleRowDto> Schedule);

    public static class GenerateScheduleEndpoint
    {
        public static IEndpointRouteBuilder MapGenerateSchedule(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/scheduling/generate", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.AcademicHead)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            GenerateScheduleRequest request,
            AppDbContext db,
            CspScheduler scheduler,
            AuditLog audit,
            CancellationToken cancellationToken)
        {
            var semester = request.SemesterId is { } id
                ? await db.Semesters.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                : await db.Semesters.FirstOrDefaultAsync(s => s.IsActive, cancellationToken);

            if (semester is null)
            {
                return Results.BadRequest(new { message = "No target semester found. Activate a semester or pass a valid semesterId." });
            }

            var sections = await db.Sections
                .Include(s => s.Subject)
                .Where(s => s.SemesterId == semester.Id)
                .ToListAsync(cancellationToken);

            if (sections.Count == 0)
            {
                return Results.BadRequest(new { message = $"No sections are configured for {semester.Name}." });
            }

            var rooms = await db.Rooms.ToListAsync(cancellationToken);
            var timeSlots = await db.TimeSlots.ToListAsync(cancellationToken);
            var faculty = await db.FacultyProfiles.ToListAsync(cancellationToken);

            var problem = new ScheduleProblem
            {
                Sections = sections.Select(s => new SectionVar(
                    s.Id,
                    s.SectionCode,
                    s.ProgramCode,
                    s.CohortKey,
                    s.Capacity,
                    s.Subject?.Units ?? 0,
                    s.Subject?.RequiresLaboratory ?? false)).ToList(),
                Rooms = rooms.Select(r => new RoomOption(r.Id, r.Capacity, r.IsLaboratory)).ToList(),
                TimeSlots = timeSlots,
                Faculty = faculty.Select(f => new FacultyOption(f.Id, f.ProgramCode, f.MaxLoadUnits)).ToList()
            };

            var result = scheduler.Solve(problem);
            if (!result.Success)
            {
                // 422: inputs are valid but no conflict-free schedule could be produced.
                return Results.UnprocessableEntity(new
                {
                    message = "Could not generate a conflict-free schedule.",
                    reasons = result.UnplacedReasons,
                    steps = result.Steps
                });
            }

            // Replace any previously generated-but-unpublished draft; never disturb published rows.
            var existingDraft = await db.ScheduleAssignments
                .Where(a => a.SemesterId == semester.Id && !a.IsPublished)
                .ToListAsync(cancellationToken);
            db.ScheduleAssignments.RemoveRange(existingDraft);

            foreach (var a in result.Assignments)
            {
                db.ScheduleAssignments.Add(new ScheduleAssignment
                {
                    SemesterId = semester.Id,
                    SectionId = a.SectionId,
                    RoomId = a.RoomId,
                    TimeSlotId = a.TimeSlotId,
                    FacultyProfileId = a.FacultyProfileId
                });
            }

            audit.Record(AuditAction.ScheduleGenerated,
                $"Generated a conflict-free schedule for {semester.Name}: " +
                $"{result.Assignments.Count} of {sections.Count} sections placed in {result.Steps:N0} search steps.",
                "Semester", semester.Id.ToString());

            await db.SaveChangesAsync(cancellationToken);

            var schedule = await LoadScheduleAsync(db, semester.Id, cancellationToken);

            return Results.Ok(new GenerateScheduleResponse(
                semester.Id,
                semester.Name,
                sections.Count,
                result.Assignments.Count,
                result.Steps,
                schedule));
        }

        private static async Task<List<ScheduleRowDto>> LoadScheduleAsync(
            AppDbContext db, Guid semesterId, CancellationToken cancellationToken)
        {
            var rows = await db.ScheduleAssignments
                .Where(a => a.SemesterId == semesterId)
                .Include(a => a.Section).ThenInclude(s => s!.Subject)
                .Include(a => a.Room)
                .Include(a => a.TimeSlot)
                .Include(a => a.FacultyProfile).ThenInclude(f => f!.User)
                .ToListAsync(cancellationToken);

            return rows
                .OrderBy(a => a.Section!.CohortKey)
                .ThenBy(a => a.TimeSlot!.Day)
                .ThenBy(a => a.TimeSlot!.StartMinutes)
                .Select(ScheduleRowDto.From)
                .ToList();
        }
    }
}
