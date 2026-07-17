using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Documents;

namespace SENGENSystem.Server.Features.Dashboard
{
    // Vertical slice: live, semester-scoped administrative metrics (FR-DASH-01/02) — enrollment
    // and enlistment statistics, per-section seat counts, room utilization, faculty load with
    // imbalance flags (FR-FAC-04), and document completion rates (FR-DOC-04). Defaults to the
    // active semester; any semester can be selected.
    public static class GetDashboardMetricsEndpoint
    {
        /// <summary>The schedule-board week: Mon–Fri, 07:00–18:00 — the denominator for room utilization.</summary>
        private const double SchedulableHoursPerWeek = 5 * 11.0;

        public static IEndpointRouteBuilder MapDashboardMetrics(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/dashboard/metrics", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.SchoolAdmin), nameof(UserRole.AcademicHead),
                    nameof(UserRole.Registrar), nameof(UserRole.AdmissionOfficer)));
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
                return Results.Ok(new { semesterId = (Guid?)null, semesterName = (string?)null, semesters });
            }

            // ---- Registration & documents (FR-DASH-02, FR-DOC-04) ----
            var registrations = await db.StudentRegistrations.AsNoTracking()
                .Where(r => r.SemesterId == semester.Id)
                .Include(r => r.Documents)
                .ToListAsync(cancellationToken);

            var docsComplete = registrations.Count(r => DocumentChecklist.IsComplete(r.Documents));
            var registration = new
            {
                total = registrations.Count,
                submitted = registrations.Count(r => r.Status == RegistrationStatus.Submitted),
                confirmed = registrations.Count(r => r.Status == RegistrationStatus.Confirmed),
                rejected = registrations.Count(r => r.Status == RegistrationStatus.Rejected),
                preAuthorized = registrations.Count(r => r.IsPreAuthorized),
                linkedAccounts = registrations.Count(r => r.UserId != null)
            };
            var documents = new
            {
                complete = docsComplete,
                incomplete = registrations.Count - docsComplete,
                completionRatePct = registrations.Count == 0
                    ? 0
                    : Math.Round(100.0 * docsComplete / registrations.Count, 1)
            };

            // ---- Intake trend (FR-DASH-02): daily new registrations over the last 30 days,
            // with a running total so the dashboard can chart momentum, not just today's count.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var windowStart = today.AddDays(-29);
            var byDay = registrations
                .GroupBy(r => DateOnly.FromDateTime(r.CreatedAtUtc))
                .ToDictionary(g => g.Key, g => g.Count());
            var runningTotal = registrations.Count(r => DateOnly.FromDateTime(r.CreatedAtUtc) < windowStart);
            var intakeTrend = new List<object>(30);
            for (var day = windowStart; day <= today; day = day.AddDays(1))
            {
                var count = byDay.GetValueOrDefault(day);
                runningTotal += count;
                intakeTrend.Add(new { date = day.ToString("yyyy-MM-dd"), count, cumulative = runningTotal });
            }

            // ---- Enlistment (FR-DASH-02 counts by section) ----
            var slotRequests = await db.SlotRequests.AsNoTracking()
                .Where(r => r.Section!.SemesterId == semester.Id)
                .ToListAsync(cancellationToken);

            var scheduledSectionIds = await db.ScheduleAssignments.AsNoTracking()
                .Where(a => a.SemesterId == semester.Id)
                .Select(a => a.SectionId)
                .Distinct()
                .ToListAsync(cancellationToken);
            var sections = await db.Sections.AsNoTracking()
                .Where(s => s.SemesterId == semester.Id && scheduledSectionIds.Contains(s.Id))
                .Include(s => s.Subject)
                .ToListAsync(cancellationToken);

            var enlistment = new
            {
                pending = slotRequests.Count(r => r.Status == SlotRequestStatus.Requested),
                approved = slotRequests.Count(r => r.Status == SlotRequestStatus.Approved),
                rejected = slotRequests.Count(r => r.Status == SlotRequestStatus.Rejected),
                sections = sections
                    .OrderByDescending(s => s.Capacity == 0 ? 0 : (double)s.EnrolledCount / s.Capacity)
                    .ThenBy(s => s.Subject?.Code)
                    .Select(s => new
                    {
                        sectionCode = s.SectionCode,
                        subjectCode = s.Subject?.Code ?? string.Empty,
                        cohort = s.CohortKey,
                        enrolled = s.EnrolledCount,
                        capacity = s.Capacity,
                        fillPct = s.Capacity == 0 ? 0 : Math.Round(100.0 * s.EnrolledCount / s.Capacity, 1)
                    })
                    .ToList()
            };

            // ---- Rooms & faculty (FR-DASH-02 utilization + load, FR-FAC-04) ----
            var assignments = await db.ScheduleAssignments.AsNoTracking()
                .Where(a => a.SemesterId == semester.Id)
                .Include(a => a.Room)
                .Include(a => a.TimeSlot)
                .ToListAsync(cancellationToken);

            var rooms = await db.Rooms.AsNoTracking().OrderBy(r => r.Name).ToListAsync(cancellationToken);
            var hoursByRoom = assignments
                .Where(a => a.TimeSlot is not null)
                .GroupBy(a => a.RoomId)
                .ToDictionary(g => g.Key, g => g.Sum(a => a.TimeSlot!.EndMinutes - a.TimeSlot.StartMinutes) / 60.0);
            var roomUtilization = rooms.Select(r => new
            {
                room = r.Name,
                isLaboratory = r.IsLaboratory,
                capacity = r.Capacity,
                classes = assignments.Count(a => a.RoomId == r.Id),
                hoursPerWeek = Math.Round(hoursByRoom.GetValueOrDefault(r.Id), 1),
                utilizationPct = Math.Round(100.0 * hoursByRoom.GetValueOrDefault(r.Id) / SchedulableHoursPerWeek, 1)
            }).ToList();

            var facultyProfiles = await db.FacultyProfiles.AsNoTracking()
                .Include(f => f.User)
                .OrderBy(f => f.User!.LastName)
                .ToListAsync(cancellationToken);
            var unitsByFaculty = (await db.FacultyLoadAssignments.AsNoTracking()
                .Where(l => l.SemesterId == semester.Id)
                .Join(db.Subjects, l => l.SubjectId, s => s.Id, (l, s) => new { l.FacultyProfileId, s.Units })
                .ToListAsync(cancellationToken))
                .GroupBy(x => x.FacultyProfileId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Units));
            var scheduledHoursByFaculty = assignments
                .Where(a => a.TimeSlot is not null)
                .GroupBy(a => a.FacultyProfileId)
                .ToDictionary(g => g.Key, g => g.Sum(a => a.TimeSlot!.EndMinutes - a.TimeSlot.StartMinutes) / 60.0);

            var loaded = facultyProfiles.Where(f => unitsByFaculty.GetValueOrDefault(f.Id) > 0).ToList();
            var meanUnits = loaded.Count == 0 ? 0 : loaded.Average(f => unitsByFaculty.GetValueOrDefault(f.Id));
            var facultyLoad = facultyProfiles.Select(f =>
            {
                var units = unitsByFaculty.GetValueOrDefault(f.Id);
                string flag;
                if (units > f.MaxLoadUnits) flag = "Overloaded";
                else if (meanUnits > 0 && units > meanUnits * 1.5) flag = "AboveAverage";
                else if (meanUnits > 0 && units > 0 && units < meanUnits * 0.5) flag = "BelowAverage";
                else if (units == 0) flag = "Unassigned";
                else flag = "Balanced";
                return new
                {
                    name = f.User?.FullName ?? "(unknown)",
                    programCode = f.ProgramCode,
                    assignedUnits = units,
                    maxLoadUnits = f.MaxLoadUnits,
                    scheduledHours = Math.Round(scheduledHoursByFaculty.GetValueOrDefault(f.Id), 1),
                    flag
                };
            }).ToList();

            return Results.Ok(new
            {
                semesterId = semester.Id,
                semesterName = semester.Name,
                semesters,
                registration,
                documents,
                intakeTrend,
                enlistment,
                roomUtilization,
                facultyLoad = new
                {
                    meanUnits = Math.Round(meanUnits, 1),
                    members = facultyLoad
                }
            });
        }
    }
}
