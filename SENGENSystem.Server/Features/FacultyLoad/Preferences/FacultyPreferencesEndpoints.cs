using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.FacultyLoad.Preferences
{
    // Vertical slice: the Academic Head records each faculty member's preferred teaching
    // windows (FR-SCHED-03). The CSP engine rewards placements inside these windows and
    // penalizes those outside — as a soft constraint, never overriding hard ones.
    public record PreferenceWindowDto(string Day, int StartMinutes, int EndMinutes);

    public record SavePreferencesRequest(List<PreferenceWindowDto>? Windows);

    public static class FacultyPreferencesEndpoints
    {
        public static IEndpointRouteBuilder MapFacultyPreferences(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/faculty-load/{facultyProfileId:guid}/preferences")
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.AcademicHead), nameof(UserRole.SchoolAdmin)));

            group.MapGet("", GetAsync);
            group.MapPut("", SaveAsync);
            return app;
        }

        private static async Task<IResult> GetAsync(
            Guid facultyProfileId, AppDbContext db, CancellationToken cancellationToken)
        {
            var faculty = await db.FacultyProfiles.AsNoTracking()
                .Include(f => f.User)
                .FirstOrDefaultAsync(f => f.Id == facultyProfileId, cancellationToken);
            if (faculty is null)
            {
                return Results.NotFound(new { message = "Faculty member not found." });
            }

            var windows = await db.FacultyTimePreferences.AsNoTracking()
                .Where(p => p.FacultyProfileId == facultyProfileId)
                .OrderBy(p => p.Day).ThenBy(p => p.StartMinutes)
                .Select(p => new PreferenceWindowDto(p.Day.ToString(), p.StartMinutes, p.EndMinutes))
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                facultyProfileId,
                facultyName = faculty.User?.FullName ?? "(unknown)",
                windows
            });
        }

        private static async Task<IResult> SaveAsync(
            Guid facultyProfileId,
            SavePreferencesRequest request,
            AppDbContext db,
            AuditLog audit,
            CancellationToken cancellationToken)
        {
            var faculty = await db.FacultyProfiles
                .Include(f => f.User)
                .FirstOrDefaultAsync(f => f.Id == facultyProfileId, cancellationToken);
            if (faculty is null)
            {
                return Results.NotFound(new { message = "Faculty member not found." });
            }

            var incoming = request.Windows ?? [];
            var parsed = new List<FacultyTimePreference>();
            var errors = new List<string>();
            foreach (var (window, index) in incoming.Select((w, i) => (w, i)))
            {
                if (!Enum.TryParse<DayOfWeek>(window.Day, ignoreCase: true, out var day))
                {
                    errors.Add($"Window {index + 1}: unknown day \"{window.Day}\".");
                    continue;
                }
                if (window.StartMinutes < 0 || window.EndMinutes > 24 * 60 || window.StartMinutes >= window.EndMinutes)
                {
                    errors.Add($"Window {index + 1}: start must come before end within the day.");
                    continue;
                }
                parsed.Add(new FacultyTimePreference
                {
                    FacultyProfileId = facultyProfileId,
                    Day = day,
                    StartMinutes = window.StartMinutes,
                    EndMinutes = window.EndMinutes
                });
            }
            if (errors.Count > 0)
            {
                return Results.BadRequest(new { message = "Some windows are invalid.", reasons = errors });
            }

            // Replace-the-set semantics, same as faculty load reconciliation.
            var existing = await db.FacultyTimePreferences
                .Where(p => p.FacultyProfileId == facultyProfileId)
                .ToListAsync(cancellationToken);
            db.FacultyTimePreferences.RemoveRange(existing);
            db.FacultyTimePreferences.AddRange(parsed);

            audit.Record(AuditAction.FacultyPreferencesSaved,
                $"Set {parsed.Count} preferred teaching window(s) for {faculty.User?.FullName ?? facultyProfileId.ToString()}.",
                "FacultyProfile", facultyProfileId.ToString());
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new
            {
                facultyProfileId,
                windows = parsed
                    .OrderBy(p => p.Day).ThenBy(p => p.StartMinutes)
                    .Select(p => new PreferenceWindowDto(p.Day.ToString(), p.StartMinutes, p.EndMinutes))
                    .ToList()
            });
        }
    }
}
