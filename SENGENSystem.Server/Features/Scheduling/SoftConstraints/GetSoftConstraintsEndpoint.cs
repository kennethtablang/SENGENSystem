using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Scheduling.SoftConstraints
{
    // Vertical slice: the inputs the CSP engine optimises its soft constraints against, surfaced
    // so the Academic Head sees the basis behind a generated schedule and can correct it before
    // regenerating (FR-SCHED-03/-08):
    //   S1 — faculty preferred teaching windows (rewarded when honoured),
    //   S3 — the load allocation across faculty (the engine cannot rebalance it; it only reports).
    // S2 (idle gaps) has no pre-generation input — it is measured on the produced timetable — so it
    // is reported in the generation result, not here.
    public record PreferredWindowDto(string Day, int StartMinutes, int EndMinutes);

    public record FacultyPreferenceDto(
        Guid FacultyProfileId,
        string Name,
        string EmployeeId,
        IReadOnlyList<PreferredWindowDto> Windows);

    public record FacultyLoadRowDto(
        Guid FacultyProfileId,
        string Name,
        string EmployeeId,
        int AllocatedUnits,
        int MaxLoadUnits,
        int SectionCount);

    public static class GetSoftConstraintsEndpoint
    {
        public static IEndpointRouteBuilder MapSoftConstraints(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/scheduling/soft-constraints", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.AcademicHead), nameof(UserRole.SchoolAdmin)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            Guid? semesterId, AppDbContext db, CancellationToken ct)
        {
            var semester = semesterId is { } id
                ? await db.Semesters.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct)
                : await db.Semesters.AsNoTracking().FirstOrDefaultAsync(s => s.IsActive, ct);
            if (semester is null)
            {
                return Results.BadRequest(new { message = "No target semester found." });
            }

            // The faculty the engine will actually schedule this term are those with a load
            // allocation — restrict both soft-constraint views to them so the data is relevant.
            var loads = await db.FacultyLoadAssignments.AsNoTracking()
                .Where(l => l.SemesterId == semester.Id)
                .Include(l => l.Subject)
                .Include(l => l.FacultyProfile).ThenInclude(f => f!.User)
                .ToListAsync(ct);

            var facultyIds = loads.Select(l => l.FacultyProfileId).Distinct().ToList();

            var prefs = await db.FacultyTimePreferences.AsNoTracking()
                .Where(p => facultyIds.Contains(p.FacultyProfileId))
                .OrderBy(p => p.Day).ThenBy(p => p.StartMinutes)
                .ToListAsync(ct);
            var prefsByFaculty = prefs.GroupBy(p => p.FacultyProfileId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // One row per faculty member with load, ordered by name for a stable, readable list.
            var byFaculty = loads
                .GroupBy(l => l.FacultyProfileId)
                .Select(g =>
                {
                    var profile = g.First().FacultyProfile;
                    return new
                    {
                        Id = g.Key,
                        Name = profile?.User?.FullName ?? "(unknown)",
                        EmployeeId = profile?.EmployeeId ?? string.Empty,
                        MaxLoadUnits = profile?.MaxLoadUnits ?? 0,
                        AllocatedUnits = g.Sum(l => l.Subject?.Units ?? 0),
                        SectionCount = g.Count()
                    };
                })
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var preferences = byFaculty
                .Select(f => new FacultyPreferenceDto(
                    f.Id, f.Name, f.EmployeeId,
                    prefsByFaculty.TryGetValue(f.Id, out var w)
                        ? w.Select(p => new PreferredWindowDto(p.Day.ToString(), p.StartMinutes, p.EndMinutes)).ToList()
                        : []))
                .ToList();

            var loadRows = byFaculty
                .Select(f => new FacultyLoadRowDto(
                    f.Id, f.Name, f.EmployeeId, f.AllocatedUnits, f.MaxLoadUnits, f.SectionCount))
                .ToList();

            // S3 equity summary — the same relative test the optimisation report uses: one member
            // carrying at least double another (with a small absolute floor) reads as "uneven".
            var units = loadRows.Select(r => r.AllocatedUnits).ToList();
            var min = units.Count == 0 ? 0 : units.Min();
            var max = units.Count == 0 ? 0 : units.Max();
            var mean = units.Count == 0 ? 0 : units.Average();
            var spread = units.Count == 0
                ? 0
                : Math.Round(Math.Sqrt(units.Sum(u => (u - mean) * (u - mean)) / units.Count), 1);
            var looksUneven = min > 0 && max >= min * 2 && max - min >= 3;

            return Results.Ok(new
            {
                semesterId = semester.Id,
                semesterName = semester.Name,
                facultyCount = loadRows.Count,
                withPreferences = preferences.Count(p => p.Windows.Count > 0),
                preferences,
                load = new
                {
                    rows = loadRows,
                    minUnits = min,
                    maxUnits = max,
                    spread,
                    looksUneven
                }
            });
        }
    }
}
