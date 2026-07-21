using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Scheduling.GetSchedule
{
    // Vertical slice: staff review of a semester's generated schedule before publishing
    // (FR-SCHED-06). Enlistment's student-facing "browse published schedules" is a separate slice.
    public static class GetScheduleEndpoint
    {
        public static IEndpointRouteBuilder MapGetSchedule(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/scheduling/schedule", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.AcademicHead), nameof(UserRole.Registrar), nameof(UserRole.SchoolAdmin)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            Guid? semesterId,
            AppDbContext db,
            CancellationToken cancellationToken)
        {
            var semester = semesterId is { } id
                ? await db.Semesters.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                : await db.Semesters.FirstOrDefaultAsync(s => s.IsActive, cancellationToken);

            if (semester is null)
            {
                return Results.BadRequest(new { message = "No target semester found." });
            }

            var rows = await db.ScheduleAssignments
                .Where(a => a.SemesterId == semester.Id)
                .Include(a => a.Section).ThenInclude(s => s!.Subject)
                .Include(a => a.Room)
                .Include(a => a.TimeSlot)
                .Include(a => a.FacultyProfile).ThenInclude(f => f!.User)
                .ToListAsync(cancellationToken);

            var schedule = rows
                .OrderBy(a => a.Section!.CohortKey)
                .ThenBy(a => a.TimeSlot!.Day)
                .ThenBy(a => a.TimeSlot!.StartMinutes)
                .Select(ScheduleRowDto.From)
                .ToList();

            // Draft = unpublished rows; the draft is "finalized" as a whole only when every
            // draft row is locked. Drives the Finalize / Reopen control on the client.
            var draft = rows.Where(a => !a.IsPublished).ToList();
            var isFinalized = draft.Count > 0 && draft.All(a => a.IsFinalized);
            var publishedCount = rows.Count - draft.Count;

            return Results.Ok(new
            {
                semesterId = semester.Id,
                semesterName = semester.Name,
                count = schedule.Count,
                isFinalized,
                draftCount = draft.Count,
                publishedCount,
                schedule
            });
        }
    }
}
