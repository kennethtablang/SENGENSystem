using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Scheduling.MySchedule
{
    // Vertical slice: the signed-in user's own weekly timetable for a semester. Read-only.
    // Faculty see their assigned schedule with live section enrolment counts (FR-FAC-05);
    // students see the published classes of their Registrar-approved enlistments (FR-ENL).
    public record MyScheduleEntryDto(
        Guid AssignmentId,
        int Day,            // DayOfWeek as int (Monday = 1 … Friday = 5)
        int StartMinutes,
        int EndMinutes,
        Guid SubjectId,
        string SubjectCode,
        string SubjectTitle,
        string Room,
        string CohortLabel,
        string FacultyName,
        int Capacity,
        int Enrolled,
        bool IsPublished);

    public static class MyScheduleEndpoint
    {
        public static IEndpointRouteBuilder MapMySchedule(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/scheduling/my-schedule", HandleAsync).RequireAuthorization();
            return app;
        }

        private static async Task<IResult> HandleAsync(
            Guid? semesterId, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
        {
            if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return Results.Unauthorized();
            }

            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
            if (user is null) return Results.Unauthorized();

            var semester = semesterId is { } id
                ? await db.Semesters.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct)
                : await db.Semesters.AsNoTracking().FirstOrDefaultAsync(s => s.IsActive, ct)
                    ?? await db.Semesters.AsNoTracking().OrderByDescending(s => s.StartDate).FirstOrDefaultAsync(ct);

            var role = user.Role.ToString();

            if (semester is null)
            {
                return Results.Ok(Empty(role, null, null, "No active semester has been set up yet."));
            }

            List<ScheduleAssignment> rows;
            if (user.Role == UserRole.FacultyMember)
            {
                var profile = await db.FacultyProfiles.AsNoTracking().FirstOrDefaultAsync(f => f.UserId == userId, ct);
                if (profile is null)
                {
                    return Results.Ok(Empty(role, semester.Id, semester.Name, "No faculty profile is linked to your account."));
                }

                rows = await db.ScheduleAssignments.AsNoTracking()
                    .Where(a => a.SemesterId == semester.Id && a.FacultyProfileId == profile.Id)
                    .Include(a => a.Section).ThenInclude(s => s!.Subject)
                    .Include(a => a.Room)
                    .Include(a => a.TimeSlot)
                    .Include(a => a.FacultyProfile).ThenInclude(f => f!.User)
                    .ToListAsync(ct);
            }
            else if (user.Role == UserRole.Student)
            {
                // A student's timetable = the published classes of their approved enlistments (FR-ENL).
                var registration = await db.StudentRegistrations.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.UserId == userId, ct);
                if (registration is null)
                {
                    return Results.Ok(Empty(role, semester.Id, semester.Name,
                        "Link your account to your student record (Document requirements page) to see your timetable."));
                }

                var approvedSectionIds = await db.SlotRequests.AsNoTracking()
                    .Where(r => r.StudentRegistrationId == registration.Id
                        && r.Status == SlotRequestStatus.Approved)
                    .Select(r => r.SectionId)
                    .ToListAsync(ct);

                if (approvedSectionIds.Count == 0)
                {
                    return Results.Ok(Empty(role, semester.Id, semester.Name,
                        "Your class schedule will appear here once your subject enlistments are approved by the Registrar."));
                }

                rows = await db.ScheduleAssignments.AsNoTracking()
                    .Where(a => a.SemesterId == semester.Id
                        && a.IsPublished
                        && approvedSectionIds.Contains(a.SectionId))
                    .Include(a => a.Section).ThenInclude(s => s!.Subject)
                    .Include(a => a.Room)
                    .Include(a => a.TimeSlot)
                    .Include(a => a.FacultyProfile).ThenInclude(f => f!.User)
                    .ToListAsync(ct);
            }
            else
            {
                return Results.Ok(Empty(role, semester.Id, semester.Name,
                    "This page shows a faculty member’s or student’s own weekly timetable."));
            }

            var entries = rows
                .Where(a => a.Section is not null && a.TimeSlot is not null)
                .OrderBy(a => a.TimeSlot!.Day).ThenBy(a => a.TimeSlot!.StartMinutes)
                .Select(a => new MyScheduleEntryDto(
                    a.Id,
                    (int)a.TimeSlot!.Day,
                    a.TimeSlot.StartMinutes,
                    a.TimeSlot.EndMinutes,
                    a.Section!.SubjectId,
                    a.Section.Subject?.Code ?? string.Empty,
                    a.Section.Subject?.Title ?? string.Empty,
                    a.Room?.Name ?? string.Empty,
                    $"{a.Section.ProgramCode} {a.Section.YearLevel}-{a.Section.Block}",
                    a.FacultyProfile?.User?.FullName ?? string.Empty,
                    a.Section.Capacity,
                    a.Section.EnrolledCount,
                    a.IsPublished))
                .ToList();

            var totalHours = entries.Sum(e => e.EndMinutes - e.StartMinutes) / 60.0;
            var allPublished = entries.Count > 0 && entries.All(e => e.IsPublished);

            return Results.Ok(new
            {
                role,
                semesterId = semester.Id,
                semesterName = semester.Name,
                count = entries.Count,
                totalHours,
                isPublished = allPublished,
                message = entries.Count == 0 ? $"You have no classes scheduled for {semester.Name} yet." : (string?)null,
                entries
            });
        }

        private static object Empty(string role, Guid? semesterId, string? semesterName, string message) => new
        {
            role,
            semesterId,
            semesterName,
            count = 0,
            totalHours = 0.0,
            isPublished = false,
            message,
            entries = Array.Empty<MyScheduleEntryDto>()
        };
    }
}
