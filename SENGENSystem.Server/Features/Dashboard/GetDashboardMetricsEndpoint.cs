using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Analytics.RoomUtilization;
using SENGENSystem.Server.Features.Documents;

namespace SENGENSystem.Server.Features.Dashboard
{
    // Vertical slice: live, semester-scoped administrative metrics (FR-DASH-01/02) — enrollment
    // and enlistment statistics, per-section seat counts, room utilization, faculty load with
    // imbalance flags (FR-FAC-04), and document completion rates (FR-DOC-04). Defaults to the
    // active semester; any semester can be selected.
    public static class GetDashboardMetricsEndpoint
    {
        /// <summary>
        /// The institutional utilization window: Mon–Fri, 08:00–17:00 — 9 h/day × 5 days = 45 h.
        /// Shared with the Room Utilization Analysis page and the exported reports rather than
        /// re-declared here, so the dashboard card and that page can never disagree about what
        /// a utilization percentage means.
        /// </summary>
        private const double SchedulableHoursPerWeek =
            RoomUtilizationAnalysisEndpoint.SchedulableHoursPerWeek;

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

            var requirementCatalog = await DocumentChecklist.LoadCatalogAsync(db, cancellationToken);
            // Papers a student's route into the school never calls for don't count against them.
            var applicableDocuments = registrations.ToDictionary(
                r => r.Id, r => DocumentChecklist.Applicable(r, requirementCatalog));
            var docsComplete = registrations.Count(r => DocumentChecklist.IsComplete(applicableDocuments[r.Id]));
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
                // Heads, not requests: how many distinct students hold at least one seat.
                studentsEnlisted = slotRequests
                    .Where(r => r.Status == SlotRequestStatus.Approved)
                    .Select(r => r.StudentRegistrationId)
                    .Distinct()
                    .Count(),
                sections = sections
                    .OrderByDescending(s => s.Capacity == 0 ? 0 : (double)s.EnrolledCount / s.Capacity)
                    .ThenBy(s => s.Subject?.Code)
                    .Select(s => new
                    {
                        sectionCode = s.SectionCode,
                        subjectCode = s.Subject?.Code ?? string.Empty,
                        subjectTitle = s.Subject?.Title ?? string.Empty,
                        units = s.Subject?.Units ?? 0,
                        cohort = s.CohortKey,
                        enrolled = s.EnrolledCount,
                        capacity = s.Capacity,
                        free = Math.Max(0, s.Capacity - s.EnrolledCount),
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

            var rooms = await db.Rooms.AsNoTracking()
                .Include(r => r.Building)
                .OrderBy(r => r.Name)
                .ToListAsync(cancellationToken);
            // Total booked hours, and the slice inside the utilization window. Only the second
            // drives the percentage — see the note on SchedulableHoursPerWeek above.
            var hoursByRoom = assignments
                .Where(a => a.TimeSlot is not null)
                .GroupBy(a => a.RoomId)
                .ToDictionary(g => g.Key, g => g.Sum(a => a.TimeSlot!.EndMinutes - a.TimeSlot.StartMinutes) / 60.0);
            var windowHoursByRoom = assignments
                .Where(a => a.TimeSlot is not null
                    && RoomUtilizationAnalysisEndpoint.IsSchedulableDay(a.TimeSlot!.Day))
                .GroupBy(a => a.RoomId)
                .ToDictionary(g => g.Key, g => g.Sum(a => Math.Max(0,
                    Math.Min(a.TimeSlot!.EndMinutes, RoomUtilizationAnalysisEndpoint.WindowEndMinutes)
                    - Math.Max(a.TimeSlot.StartMinutes, RoomUtilizationAnalysisEndpoint.WindowStartMinutes))) / 60.0);
            var roomUtilization = rooms.Select(r => new
            {
                room = r.Name,
                building = r.Building?.Name ?? "Unassigned",
                isLaboratory = r.IsLaboratory,
                capacity = r.Capacity,
                classes = assignments.Count(a => a.RoomId == r.Id),
                hoursPerWeek = Math.Round(hoursByRoom.GetValueOrDefault(r.Id), 1),
                windowHoursPerWeek = Math.Round(windowHoursByRoom.GetValueOrDefault(r.Id), 1),
                utilizationPct = Math.Round(
                    100.0 * windowHoursByRoom.GetValueOrDefault(r.Id) / SchedulableHoursPerWeek, 1)
            }).ToList();

            // ---- Seat supply & demand: the enlistment picture as one number set. ----
            var seatCapacity = sections.Sum(s => s.Capacity);
            var seatsTaken = sections.Sum(s => s.EnrolledCount);
            var seats = new
            {
                capacity = seatCapacity,
                taken = seatsTaken,
                free = Math.Max(0, seatCapacity - seatsTaken),
                fillPct = seatCapacity == 0 ? 0 : Math.Round(100.0 * seatsTaken / seatCapacity, 1),
                sectionsFull = sections.Count(s => s.Capacity > 0 && s.EnrolledCount >= s.Capacity),
                sectionsEmpty = sections.Count(s => s.EnrolledCount == 0)
            };

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

            // ---- Scheduling coverage: how much of the term's teaching is actually placed
            // on the board, and how much of it students can already see (FR-SCHED-06).
            var allSections = await db.Sections.AsNoTracking()
                .Where(s => s.SemesterId == semester.Id)
                .CountAsync(cancellationToken);
            var schedule = new
            {
                assignments = assignments.Count,
                published = assignments.Count(a => a.IsPublished),
                draft = assignments.Count(a => !a.IsPublished),
                manualOverrides = assignments.Count(a => a.IsManualOverride),
                sectionsScheduled = scheduledSectionIds.Count,
                sectionsTotal = allSections,
                sectionsUnscheduled = Math.Max(0, allSections - scheduledSectionIds.Count),
                coveragePct = allSections == 0 ? 0 : Math.Round(100.0 * scheduledSectionIds.Count / allSections, 1),
                roomsUsed = assignments.Select(a => a.RoomId).Distinct().Count(),
                isPublished = assignments.Count > 0 && assignments.All(a => a.IsPublished)
            };

            // ---- Inventory: the master data the scheduling engine draws on. Gives the
            // administrator a one-glance census of the system's configured scope.
            var inventory = new
            {
                curricula = await db.Curricula.CountAsync(c => !c.IsArchived, cancellationToken),
                curriculaArchived = await db.Curricula.CountAsync(c => c.IsArchived, cancellationToken),
                subjects = await db.Subjects.CountAsync(s => !s.IsArchived, cancellationToken),
                subjectsArchived = await db.Subjects.CountAsync(s => s.IsArchived, cancellationToken),
                buildings = await db.Buildings.CountAsync(cancellationToken),
                rooms = rooms.Count,
                laboratories = rooms.Count(r => r.IsLaboratory),
                timeSlots = await db.TimeSlots.CountAsync(t => t.IsAllowable, cancellationToken),
                classSections = await db.ClassSections.CountAsync(c => c.SemesterId == semester.Id, cancellationToken),
                faculty = facultyProfiles.Count,
                semesters = semesters.Count,
                users = await db.Users.CountAsync(u => u.IsActive, cancellationToken),
                usersInactive = await db.Users.CountAsync(u => !u.IsActive, cancellationToken)
            };

            // ---- Recent activity: the tail of the audit trail (FR-AUD-01), so the dashboard
            // shows who is moving the numbers, not just that they moved.
            var activity = await db.AuditEntries.AsNoTracking()
                .OrderByDescending(e => e.OccurredAtUtc)
                .Take(8)
                .Select(e => new
                {
                    occurredAtUtc = e.OccurredAtUtc,
                    actor = e.ActorName,
                    role = e.ActorRole,
                    action = e.Action.ToString(),
                    summary = e.Summary
                })
                .ToListAsync(cancellationToken);

            // ---- Program mix: intake split across the programs/tracks students chose,
            // ranked so the biggest cohort reads first.
            var programMix = registrations
                .GroupBy(r => r.Program)
                .Select(g => new
                {
                    program = g.Key.ToString(),
                    total = g.Count(),
                    confirmed = g.Count(r => r.Status == RegistrationStatus.Confirmed),
                    cleared = g.Count(r => r.IsPreAuthorized)
                })
                .OrderByDescending(x => x.total)
                .ToList();

            // ---- Requirement mix: per admission paper, how far the whole cohort has got.
            // Shows *which* document is holding clearance up, not just that something is.
            var documentMix = registrations
                .SelectMany(r => applicableDocuments[r.Id])
                .GroupBy(d => d.RequirementCode)
                .Select(g => new
                {
                    document = requirementCatalog.Label(g.Key),
                    order = requirementCatalog.Order(g.Key),
                    submitted = g.Count(d => d.Status == DocumentStatus.Submitted),
                    // "Received, but not the original" — a photocopy, or a certificate of grades
                    // standing in for a transcript. Both leave the original still to come.
                    xerox = g.Count(d => d.Status is DocumentStatus.XeroxCopy or DocumentStatus.CertificateOfGrades),
                    missing = g.Count(d => d.Status == DocumentStatus.NotSubmitted),
                    total = g.Count()
                })
                .OrderBy(x => x.order)
                .Select(x => new { x.document, x.submitted, x.xerox, x.missing, x.total })
                .ToList();

            // ---- Weekly schedule density: classes per weekday per hour, for the heatmap.
            // Mon–Sat × 07:00–17:00 — Saturday carries its own slot grid (08:00–16:00 blocks),
            // so it belongs on the map even though the weekday board stops at Friday.
            var heatCells = assignments
                .Where(a => a.TimeSlot is not null)
                .SelectMany(a =>
                {
                    var startHour = a.TimeSlot!.StartMinutes / 60;
                    var endHour = (int)Math.Ceiling(a.TimeSlot.EndMinutes / 60.0);
                    return Enumerable.Range(startHour, Math.Max(1, endHour - startHour))
                        .Select(h => new { Day = (int)a.TimeSlot.Day, Hour = h });
                })
                .Where(c => c.Day is >= 1 and <= 6 && c.Hour is >= 7 and <= 17)
                .GroupBy(c => new { c.Day, c.Hour })
                .ToDictionary(g => g.Key, g => g.Count());
            var scheduleHeat = new List<object>();
            for (var day = 1; day <= 6; day++)
            {
                for (var hour = 7; hour <= 17; hour++)
                {
                    scheduleHeat.Add(new
                    {
                        day,
                        hour,
                        classes = heatCells.GetValueOrDefault(new { Day = day, Hour = hour })
                    });
                }
            }

            // ---- Term progress: where "today" sits between the semester's start and end.
            var span = semester.EndDate.DayNumber - semester.StartDate.DayNumber;
            var elapsed = today.DayNumber - semester.StartDate.DayNumber;

            return Results.Ok(new
            {
                semesterId = semester.Id,
                semesterName = semester.Name,
                semesters,
                semester = new
                {
                    term = semester.Term.ToString(),
                    startDate = semester.StartDate.ToString("yyyy-MM-dd"),
                    endDate = semester.EndDate.ToString("yyyy-MM-dd"),
                    isActive = semester.IsActive,
                    isArchived = semester.IsArchived,
                    daysElapsed = Math.Clamp(elapsed, 0, Math.Max(0, span)),
                    daysTotal = Math.Max(0, span),
                    daysRemaining = Math.Max(0, semester.EndDate.DayNumber - today.DayNumber),
                    progressPct = span <= 0 ? 0 : Math.Round(100.0 * Math.Clamp(elapsed, 0, span) / span, 1)
                },
                generatedAtUtc = DateTime.UtcNow,
                registration,
                documents,
                intakeTrend,
                enlistment,
                programMix,
                documentMix,
                scheduleHeat,
                seats,
                schedule,
                inventory,
                activity,
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
