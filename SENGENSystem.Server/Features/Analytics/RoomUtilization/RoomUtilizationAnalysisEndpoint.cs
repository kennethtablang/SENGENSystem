using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Reports;

namespace SENGENSystem.Server.Features.Analytics.RoomUtilization
{
    // Vertical slice: institution-wide classroom usage analytics (FR-DASH-02 room utilization,
    // FR-RPT-02 planning). The dashboard shows utilization as one card among many; this slice
    // is the dedicated analysis — every room scored, categorised, and filterable, so an
    // administrator can find the under-used space rather than just read an average.
    public static class RoomUtilizationAnalysisEndpoint
    {
        // ---- The utilization window (institutional standard) --------------------------------
        //
        // A teaching space is only considered "available" Monday–Friday, 08:00–17:00:
        //
        //     9 hours/day (08:00–17:00) × 5 days (Mon–Fri) = 45 schedulable hours per week
        //
        // That 45 h is the denominator behind every percentage on this page. A room holding
        // 18 hours of classes reads 40%, not "18 hours" — the window is what makes the number
        // comparable across rooms.
        //
        // Two consequences follow, and both are deliberate:
        //   1. Saturday classes and any meeting outside 08:00–17:00 do NOT raise utilization.
        //      Only the portion of a class falling inside the window is counted, so an early
        //      or late meeting cannot inflate the percentage past what the window allows.
        //   2. Utilization therefore cannot exceed 100%, which is what lets the bands below
        //      mean the same thing for every room.
        //
        // The window bounds are owned by the reports slice (ReportsEndpoints) and referenced
        // here rather than re-declared: this page and the exported .xlsx report must never
        // disagree about what a utilization percentage means.
        internal const int WindowStartMinutes = ReportsEndpoints.WindowStartMinutes; // 08:00
        internal const int WindowEndMinutes = ReportsEndpoints.WindowEndMinutes;     // 17:00

        /// <summary>Hours in one schedulable day — 08:00–17:00 is 9 hours.</summary>
        internal const double WindowHoursPerDay = (WindowEndMinutes - WindowStartMinutes) / 60.0;

        /// <summary>Schedulable days per week — Monday through Friday.</summary>
        internal const int SchedulableDaysPerWeek = 5;

        /// <summary>The denominator for every utilization percentage: 9 h/day × 5 days = 45 h.</summary>
        internal const double SchedulableHoursPerWeek = WindowHoursPerDay * SchedulableDaysPerWeek;

        /// <summary>True for Monday–Friday. Saturday teaching sits outside the utilization window.</summary>
        internal static bool IsSchedulableDay(DayOfWeek day) =>
            day is >= DayOfWeek.Monday and <= DayOfWeek.Friday;

        // Utilization bands. A room below Critical is effectively idle; above Moderate it is
        // carrying its share of the timetable and needs no attention.
        private const double CriticalCeiling = 15.0;
        private const double LowCeiling = 30.0;
        private const double ModerateCeiling = 60.0;

        public static IEndpointRouteBuilder MapRoomUtilizationAnalysis(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/analytics/room-utilization", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.SchoolAdmin), nameof(UserRole.AcademicHead), nameof(UserRole.Registrar)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            Guid? semesterId,
            AppDbContext db,
            CancellationToken cancellationToken)
        {
            var semesters = await db.Semesters.AsNoTracking()
                .OrderByDescending(s => s.StartDate)
                .Select(s => new { id = s.Id, name = s.Name, isActive = s.IsActive })
                .ToListAsync(cancellationToken);

            var semester = semesterId is { } id
                ? await db.Semesters.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                : await db.Semesters.AsNoTracking().FirstOrDefaultAsync(s => s.IsActive, cancellationToken);
            if (semester is null)
            {
                return Results.Ok(new
                {
                    semesterId = (Guid?)null,
                    semesterName = (string?)null,
                    semesters,
                    buildings = Array.Empty<string>(),
                    summary = Empty(),
                    rooms = Array.Empty<object>()
                });
            }

            // Every room is analysed, including ones the engine never placed a class in —
            // a room with zero classes is precisely the finding this page exists to surface.
            var rooms = await db.Rooms.AsNoTracking()
                .Include(r => r.Building)
                .OrderBy(r => r.Name)
                .ToListAsync(cancellationToken);

            var assignments = await db.ScheduleAssignments.AsNoTracking()
                .Where(a => a.SemesterId == semester.Id)
                .Include(a => a.TimeSlot)
                .Select(a => new { a.RoomId, a.TimeSlot })
                .ToListAsync(cancellationToken);

            // Hours are counted twice over: the room's full booked time (what a caretaker would
            // call "in use"), and the slice of it inside Mon–Fri 08:00–17:00 (what utilization
            // is measured against). Only the second drives the percentage — see the window
            // notes above — so a 07:00 or Saturday class shows in the first figure but cannot
            // push utilization past 100%.
            var byRoom = assignments
                .Where(a => a.TimeSlot is not null)
                .GroupBy(a => a.RoomId)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        Classes = g.Count(),
                        Hours = g.Sum(a => a.TimeSlot!.EndMinutes - a.TimeSlot.StartMinutes) / 60.0,
                        WindowHours = g.Where(a => IsSchedulableDay(a.TimeSlot!.Day))
                            .Sum(a => Math.Max(0,
                                Math.Min(a.TimeSlot!.EndMinutes, WindowEndMinutes)
                                - Math.Max(a.TimeSlot.StartMinutes, WindowStartMinutes))) / 60.0
                    });

            var analysed = rooms.Select(r =>
            {
                var stats = byRoom.GetValueOrDefault(r.Id);
                var hours = stats?.Hours ?? 0;
                var windowHours = stats?.WindowHours ?? 0;
                // The percentage is in-window hours over the 45 h week — never total hours.
                var utilizationPct = Math.Round(100.0 * windowHours / SchedulableHoursPerWeek, 1);
                var (level, status) = Classify(utilizationPct);
                return new
                {
                    id = r.Id,
                    room = r.Name,
                    building = r.Building?.Name ?? "Unassigned",
                    buildingCode = r.Building?.Code,
                    type = r.IsLaboratory ? "Laboratory" : "Lecture",
                    isLaboratory = r.IsLaboratory,
                    capacity = r.Capacity,
                    classes = stats?.Classes ?? 0,
                    hoursPerWeek = Math.Round(hours, 1),
                    windowHoursPerWeek = Math.Round(windowHours, 1),
                    schedulableHours = SchedulableHoursPerWeek,
                    utilizationPct,
                    level,
                    status
                };
            }).ToList();

            var summary = new
            {
                totalRooms = analysed.Count,
                averageUtilizationPct = analysed.Count == 0
                    ? 0
                    : Math.Round(analysed.Average(r => r.utilizationPct), 1),
                critical = analysed.Count(r => r.level == "Critical"),
                low = analysed.Count(r => r.level == "Low"),
                moderate = analysed.Count(r => r.level == "Moderate"),
                optimal = analysed.Count(r => r.level == "Optimal"),
                totalClasses = analysed.Sum(r => r.classes),
                totalHours = Math.Round(analysed.Sum(r => r.hoursPerWeek), 1)
            };

            return Results.Ok(new
            {
                semesterId = semester.Id,
                semesterName = semester.Name,
                semesters,
                generatedAtUtc = DateTime.UtcNow,
                // The basis of every percentage, sent to the client so the page states its
                // own methodology rather than hard-coding "45" in two places.
                window = new
                {
                    days = "Monday–Friday",
                    daysPerWeek = SchedulableDaysPerWeek,
                    startTime = "08:00",
                    endTime = "17:00",
                    hoursPerDay = WindowHoursPerDay,
                    hoursPerWeek = SchedulableHoursPerWeek,
                    label = $"Mon–Fri, 08:00–17:00 · {WindowHoursPerDay:0.#} h/day · {SchedulableHoursPerWeek:0.#} h/week"
                },
                buildings = analysed.Select(r => r.building).Distinct().OrderBy(b => b).ToList(),
                summary,
                rooms = analysed
                    .OrderBy(r => r.utilizationPct)
                    .ThenBy(r => r.room)
                    .ToList()
            });
        }

        /// <summary>Maps a utilization percentage onto its band and the administrator-facing wording.</summary>
        internal static (string Level, string Status) Classify(double utilizationPct) => utilizationPct switch
        {
            < CriticalCeiling => ("Critical", "Critically underutilized"),
            < LowCeiling => ("Low", "Low usage"),
            < ModerateCeiling => ("Moderate", "Moderately utilized"),
            _ => ("Optimal", "Well utilized")
        };

        private static object Empty() => new
        {
            totalRooms = 0,
            averageUtilizationPct = 0.0,
            critical = 0,
            low = 0,
            moderate = 0,
            optimal = 0,
            totalClasses = 0,
            totalHours = 0.0
        };
    }
}
