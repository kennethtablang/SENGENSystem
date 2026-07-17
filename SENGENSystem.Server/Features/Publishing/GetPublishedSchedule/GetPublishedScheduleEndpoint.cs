using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Features.Scheduling;

namespace SENGENSystem.Server.Features.Publishing.GetPublishedSchedule
{
    // Vertical slice: the published (finalized) schedule for a semester, viewable by every
    // authenticated role and filterable by day and by class block so it can be distributed
    // by week, by day, and by class (FR-PUB-02). Enlistment's student browse (FR-ENL-01)
    // builds on this same published-only view.
    public static class GetPublishedScheduleEndpoint
    {
        public static IEndpointRouteBuilder MapGetPublishedSchedule(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/publishing/schedule", HandleAsync).RequireAuthorization();
            return app;
        }

        private static async Task<IResult> HandleAsync(
            Guid? semesterId,
            string? day,
            string? cohort,
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

            DayOfWeek? dayFilter = null;
            if (!string.IsNullOrWhiteSpace(day))
            {
                if (!Enum.TryParse<DayOfWeek>(day, ignoreCase: true, out var parsedDay))
                {
                    return Results.BadRequest(new { message = "Unknown day filter." });
                }
                dayFilter = parsedDay;
            }

            // The published set for one semester is small (dozens of rows), so load it once
            // and filter in memory — CohortKey is computed and cannot be translated to SQL.
            var rows = await db.ScheduleAssignments
                .Where(a => a.SemesterId == semester.Id && a.IsPublished)
                .Include(a => a.Section).ThenInclude(s => s!.Subject)
                .Include(a => a.Room)
                .Include(a => a.TimeSlot)
                .Include(a => a.FacultyProfile).ThenInclude(f => f!.User)
                .ToListAsync(cancellationToken);

            // Distinct pickers for the by-day / by-class views, always from the full set
            // so the client can switch views regardless of the current filter.
            var days = rows
                .Select(a => a.TimeSlot!.Day)
                .Distinct()
                .OrderBy(d => d == DayOfWeek.Sunday ? 7 : (int)d)
                .Select(d => d.ToString())
                .ToList();
            var cohorts = rows
                .Select(a => a.Section!.CohortKey)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            var filtered = rows.AsEnumerable();
            if (dayFilter is { } wanted)
            {
                filtered = filtered.Where(a => a.TimeSlot!.Day == wanted);
            }
            if (!string.IsNullOrWhiteSpace(cohort))
            {
                filtered = filtered.Where(a =>
                    string.Equals(a.Section!.CohortKey, cohort, StringComparison.OrdinalIgnoreCase));
            }

            var schedule = filtered
                .OrderBy(a => a.Section!.CohortKey)
                .ThenBy(a => a.TimeSlot!.Day)
                .ThenBy(a => a.TimeSlot!.StartMinutes)
                .Select(ScheduleRowDto.From)
                .ToList();

            return Results.Ok(new
            {
                semesterId = semester.Id,
                semesterName = semester.Name,
                count = schedule.Count,
                days,
                cohorts,
                schedule
            });
        }
    }
}
