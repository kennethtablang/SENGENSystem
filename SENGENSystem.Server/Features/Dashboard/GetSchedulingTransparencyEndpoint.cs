using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Dashboard
{
    // Vertical slice: scheduling transparency (FR-DASH-03) — exposes, per assignment, how the
    // row came to be (engine-generated vs. manual override, draft vs. published) alongside the
    // hard constraints every row satisfies and the soft factors the engine optimizes.
    public static class GetSchedulingTransparencyEndpoint
    {
        private static readonly string[] HardConstraints =
        [
            "No room is double-booked in overlapping time slots.",
            "No faculty member teaches two sections in overlapping time slots.",
            "Sections of the same student block (cohort) never overlap in time.",
            "Room capacity is at least the section capacity, and laboratory subjects are placed in laboratories.",
            "Every faculty member stays within their maximum load units."
        ];

        private static readonly string[] SoftFactors =
        [
            "Faculty load is balanced across members (deviation from the mean load is penalized).",
            "Faculty preferred teaching windows are rewarded; placements outside them are penalized.",
            "Idle gaps between a cohort's consecutive classes are minimized."
        ];

        public static IEndpointRouteBuilder MapSchedulingTransparency(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/dashboard/scheduling-transparency", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.SchoolAdmin), nameof(UserRole.AcademicHead), nameof(UserRole.Registrar)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            Guid? semesterId,
            AppDbContext db,
            CancellationToken cancellationToken)
        {
            var semester = semesterId is { } id
                ? await db.Semesters.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                : await db.Semesters.AsNoTracking().FirstOrDefaultAsync(s => s.IsActive, cancellationToken);
            if (semester is null)
            {
                return Results.BadRequest(new { message = "No target semester found." });
            }

            var rows = await db.ScheduleAssignments.AsNoTracking()
                .Where(a => a.SemesterId == semester.Id)
                .Include(a => a.Section).ThenInclude(s => s!.Subject)
                .Include(a => a.Room)
                .Include(a => a.TimeSlot)
                .Include(a => a.FacultyProfile).ThenInclude(f => f!.User)
                .ToListAsync(cancellationToken);

            var assignments = rows
                .OrderBy(a => a.Section!.CohortKey)
                .ThenBy(a => a.TimeSlot!.Day)
                .ThenBy(a => a.TimeSlot!.StartMinutes)
                .Select(a => new
                {
                    subjectCode = a.Section?.Subject?.Code ?? string.Empty,
                    sectionCode = a.Section?.SectionCode ?? string.Empty,
                    cohort = a.Section?.CohortKey ?? string.Empty,
                    room = a.Room?.Name ?? string.Empty,
                    day = a.TimeSlot?.Day.ToString() ?? string.Empty,
                    time = a.TimeSlot is null
                        ? string.Empty
                        : $"{Format(a.TimeSlot.StartMinutes)}–{Format(a.TimeSlot.EndMinutes)}",
                    faculty = a.FacultyProfile?.User?.FullName ?? string.Empty,
                    provenance = a.IsManualOverride ? "Manual override" : "CSP engine",
                    isPublished = a.IsPublished
                })
                .ToList();

            return Results.Ok(new
            {
                semesterId = semester.Id,
                semesterName = semester.Name,
                engine = new
                {
                    kind = "Deterministic, rule-based CSP (backtracking, most-constrained-variable ordering) — no machine learning.",
                    hardConstraints = HardConstraints,
                    softFactors = SoftFactors
                },
                summary = new
                {
                    total = assignments.Count,
                    engineGenerated = assignments.Count(a => a.provenance == "CSP engine"),
                    manualOverrides = assignments.Count(a => a.provenance == "Manual override"),
                    published = assignments.Count(a => a.isPublished)
                },
                assignments
            });
        }

        private static string Format(int minutes) => $"{minutes / 60:D2}:{minutes % 60:D2}";
    }
}
