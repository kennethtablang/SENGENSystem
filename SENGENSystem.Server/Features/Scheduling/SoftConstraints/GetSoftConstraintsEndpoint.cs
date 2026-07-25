using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Scheduling.SoftConstraints
{
    /// <summary>
    /// The Academic Head's adjustment to the soft-constraint weights the CSP engine optimises
    /// against (FR-SCHED-03/-05). Each is the relative importance of one concern; the engine
    /// normalises every term to 0..1 before weighting, so raising one genuinely raises its pull.
    /// </summary>
    public record SoftConstraintWeightsRequest(
        double? Preference, double? IdleGap, double? RoomFit, double? GapSaturationHours);

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
            var group = app.MapGroup("/api/scheduling/soft-constraints")
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.AcademicHead), nameof(UserRole.SchoolAdmin)));

            group.MapGet("", HandleAsync);
            group.MapPut("/weights", SetWeightsAsync);
            return app;
        }

        // Widest sane idle-gap saturation, in hours — a whole long day.
        private const double MaxGapSaturationHours = 24.0;

        /// <summary>
        /// PUT the soft-constraint weights the engine will use on the next generation. Values are
        /// relative importances clamped to 0..1 (the engine normalises before weighting), and the
        /// idle-gap saturation is the longest gap treated as "as bad as it gets", in hours.
        /// </summary>
        private static async Task<IResult> SetWeightsAsync(
            SoftConstraintWeightsRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var errors = new Dictionary<string, string[]>();
            var preference = Weight(errors, "preference", request.Preference);
            var idleGap = Weight(errors, "idleGap", request.IdleGap);
            var roomFit = Weight(errors, "roomFit", request.RoomFit);

            var gapSaturation = request.GapSaturationHours ?? 0;
            if (gapSaturation < 1 || gapSaturation > MaxGapSaturationHours)
            {
                errors["gapSaturationHours"] = [$"Choose an idle-gap saturation between 1 and {MaxGapSaturationHours:0} hours."];
            }

            if (errors.Count == 0 && preference + idleGap + roomFit <= 0)
            {
                errors["preference"] = ["At least one weight must be greater than zero, otherwise there is nothing to optimise."];
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var settings = await db.GetSettingsForUpdateAsync(ct);
            settings.WeightPreference = preference;
            settings.WeightIdleGap = idleGap;
            settings.WeightRoomFit = roomFit;
            settings.GapSaturationHours = gapSaturation;
            settings.UpdatedAtUtc = DateTime.UtcNow;

            audit.Record(AuditAction.SoftConstraintWeightsChanged,
                $"Adjusted the scheduling soft-constraint weights — preference {preference:0.00}, " +
                $"idle gap {idleGap:0.00}, room fit {roomFit:0.00}, gap saturation {gapSaturation:0.#}h.",
                "SystemSettings", SystemSettings.SingletonId.ToString());
            await db.SaveChangesAsync(ct);

            return Results.Ok(WeightsPayload(settings));
        }

        private static double Weight(Dictionary<string, string[]> errors, string key, double? value)
        {
            if (value is not { } v || double.IsNaN(v) || v < 0 || v > 1)
            {
                errors[key] = ["Choose a weight between 0 and 1."];
                return 0;
            }
            return v;
        }

        private static object WeightsPayload(SystemSettings s) => new
        {
            preference = s.WeightPreference,
            idleGap = s.WeightIdleGap,
            roomFit = s.WeightRoomFit,
            gapSaturationHours = s.GapSaturationHours
        };

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

            var settings = await db.GetSettingsAsync(ct);

            return Results.Ok(new
            {
                semesterId = semester.Id,
                semesterName = semester.Name,
                // The tunable weights the engine will use on the next generation (FR-SCHED-05).
                weights = WeightsPayload(settings),
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
