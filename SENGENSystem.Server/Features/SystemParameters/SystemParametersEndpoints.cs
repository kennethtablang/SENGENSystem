using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.SystemParameters
{
    // Vertical slice: the School Admin tunes the institutional inputs the scheduling engine
    // runs on (FR-SCHED-05) — allowable time slots, per-faculty unit-load ceilings, and the
    // section seat cap (FR-ENL-03). Admin-only: these govern every generated schedule.
    public record SectionCapacityRequest(int? Cap);

    public record TimeSlotRequest(int? Day, int? StartMinutes, int? EndMinutes);

    public record LoadLimitRequest(int? MaxLoadUnits);

    // Only the fields supplied are changed, so the page can save one card at a time.
    public record SettingsRequest(
        bool? EnlistmentOpen,
        int? MaxEnlistmentUnitsPerStudent,
        int? MinSectionEnrollment,
        int? ScheduleTimeBudgetSeconds,
        int? ScheduleMaxStepsThousands);

    public static class SystemParametersEndpoints
    {
        /// <summary>Widest sane seat cap; guards a typo'd 4000 from reaching the database.</summary>
        private const int MaxSectionCapacityCap = 200;

        /// <summary>Widest sane teaching ceiling in subject units per semester.</summary>
        private const int MaxLoadUnitsCeiling = 60;

        /// <summary>Bounds for the engine budgets so a typo can't wedge or exhaust a run.</summary>
        private const int MinTimeBudgetSeconds = 5;
        private const int MaxTimeBudgetSeconds = 120;
        private const int MinMaxStepsThousands = 50;
        private const int MaxMaxStepsThousands = 5000;

        /// <summary>Widest sane per-student unit ceiling for a term.</summary>
        private const int MaxEnlistmentUnitsCeiling = 60;

        public static IEndpointRouteBuilder MapSystemParameters(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/parameters")
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.SchoolAdmin)));

            group.MapGet("", GetAsync);
            group.MapPut("/section-capacity", SetSectionCapacityAsync);
            group.MapPut("/settings", SetSettingsAsync);
            group.MapPost("/time-slots", CreateTimeSlotAsync);
            group.MapPut("/time-slots/{id:guid}", UpdateTimeSlotAsync);
            group.MapDelete("/time-slots/{id:guid}", DeleteTimeSlotAsync);
            group.MapPut("/faculty/{id:guid}/load-limit", SetLoadLimitAsync);
            return app;
        }

        // ---------- Overview ----------

        private static async Task<IResult> GetAsync(AppDbContext db, CancellationToken ct)
        {
            var settings = await db.GetSettingsAsync(ct);
            var cap = settings.SectionCapacityCap;

            // Sections created before the cap was lowered keep their seats (see SystemSettings);
            // surfacing the count is what lets the admin see the consequence of a lower cap.
            var sectionsAboveCap = await db.Sections.CountAsync(s => s.Capacity > cap, ct);

            var slotsInUse = (await db.ScheduleAssignments.AsNoTracking()
                    .Select(a => a.TimeSlotId)
                    .Distinct()
                    .ToListAsync(ct))
                .ToHashSet();

            var timeSlots = (await db.TimeSlots.AsNoTracking()
                    .Where(t => t.IsAllowable)
                    .OrderBy(t => t.Day).ThenBy(t => t.StartMinutes)
                    .ToListAsync(ct))
                .Select(t => new
                {
                    id = t.Id,
                    day = (int)t.Day,
                    startMinutes = t.StartMinutes,
                    endMinutes = t.EndMinutes,
                    inUse = slotsInUse.Contains(t.Id)
                })
                .ToList();

            var faculty = await LoadFacultyAsync(db, ct);

            // Sections in the active term that haven't reached the viable minimum, so the admin can
            // act (merge, promote, or move students) — advisory, mirroring the above-cap count.
            var activeSemesterId = await db.GetActiveSemesterIdAsync(ct);
            var underFilledSections = settings.MinSectionEnrollment > 0 && activeSemesterId is { } sid
                ? await db.Sections.CountAsync(
                    s => s.SemesterId == sid && s.EnrolledCount < settings.MinSectionEnrollment, ct)
                : 0;

            return Results.Ok(new
            {
                sectionCapacity = new
                {
                    cap,
                    defaultCap = Section.DefaultCapacityCap,
                    maxCap = MaxSectionCapacityCap,
                    sectionsAboveCap
                },
                enrollment = new
                {
                    enlistmentOpen = settings.EnlistmentOpen,
                    maxEnlistmentUnitsPerStudent = settings.MaxEnlistmentUnitsPerStudent,
                    maxEnlistmentUnitsCeiling = MaxEnlistmentUnitsCeiling,
                    minSectionEnrollment = settings.MinSectionEnrollment,
                    underFilledSections
                },
                engine = new
                {
                    timeBudgetSeconds = settings.ScheduleTimeBudgetSeconds,
                    minTimeBudgetSeconds = MinTimeBudgetSeconds,
                    maxTimeBudgetSeconds = MaxTimeBudgetSeconds,
                    maxStepsThousands = settings.ScheduleMaxStepsThousands,
                    minMaxStepsThousands = MinMaxStepsThousands,
                    maxMaxStepsThousands = MaxMaxStepsThousands
                },
                timeSlots,
                faculty,
                updatedAtUtc = settings.UpdatedAtUtc
            });
        }

        // ---------- Enrollment / enlistment + engine budgets ----------

        private static async Task<IResult> SetSettingsAsync(
            SettingsRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var errors = new Dictionary<string, string[]>();

            if (request.MaxEnlistmentUnitsPerStudent is { } maxUnits && (maxUnits < 0 || maxUnits > MaxEnlistmentUnitsCeiling))
            {
                errors["maxEnlistmentUnitsPerStudent"] = [$"Must be between 0 (no limit) and {MaxEnlistmentUnitsCeiling}."];
            }
            if (request.MinSectionEnrollment is { } minEnroll && (minEnroll < 0 || minEnroll > MaxSectionCapacityCap))
            {
                errors["minSectionEnrollment"] = [$"Must be between 0 and {MaxSectionCapacityCap}."];
            }
            if (request.ScheduleTimeBudgetSeconds is { } budget && (budget < MinTimeBudgetSeconds || budget > MaxTimeBudgetSeconds))
            {
                errors["scheduleTimeBudgetSeconds"] = [$"Must be between {MinTimeBudgetSeconds} and {MaxTimeBudgetSeconds} seconds."];
            }
            if (request.ScheduleMaxStepsThousands is { } steps && (steps < MinMaxStepsThousands || steps > MaxMaxStepsThousands))
            {
                errors["scheduleMaxStepsThousands"] = [$"Must be between {MinMaxStepsThousands} and {MaxMaxStepsThousands} (thousand steps)."];
            }
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var settings = await db.GetSettingsForUpdateAsync(ct);
            var changes = new List<string>();

            if (request.EnlistmentOpen is { } open && open != settings.EnlistmentOpen)
            {
                settings.EnlistmentOpen = open;
                changes.Add(open ? "opened enlistment" : "closed enlistment");
            }
            if (request.MaxEnlistmentUnitsPerStudent is { } mu && mu != settings.MaxEnlistmentUnitsPerStudent)
            {
                settings.MaxEnlistmentUnitsPerStudent = mu;
                changes.Add($"per-student unit ceiling → {(mu == 0 ? "no limit" : mu.ToString())}");
            }
            if (request.MinSectionEnrollment is { } me && me != settings.MinSectionEnrollment)
            {
                settings.MinSectionEnrollment = me;
                changes.Add($"minimum section size → {me}");
            }
            if (request.ScheduleTimeBudgetSeconds is { } tb && tb != settings.ScheduleTimeBudgetSeconds)
            {
                settings.ScheduleTimeBudgetSeconds = tb;
                changes.Add($"engine time budget → {tb}s");
            }
            if (request.ScheduleMaxStepsThousands is { } ms && ms != settings.ScheduleMaxStepsThousands)
            {
                settings.ScheduleMaxStepsThousands = ms;
                changes.Add($"engine step budget → {ms}k");
            }

            if (changes.Count > 0)
            {
                settings.UpdatedAtUtc = DateTime.UtcNow;
                audit.Record(AuditAction.SystemParametersUpdated,
                    $"Updated system parameters: {string.Join("; ", changes)}.",
                    "SystemSettings", SystemSettings.SingletonId.ToString());
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new
            {
                enlistmentOpen = settings.EnlistmentOpen,
                maxEnlistmentUnitsPerStudent = settings.MaxEnlistmentUnitsPerStudent,
                minSectionEnrollment = settings.MinSectionEnrollment,
                timeBudgetSeconds = settings.ScheduleTimeBudgetSeconds,
                maxStepsThousands = settings.ScheduleMaxStepsThousands
            });
        }

        /// <summary>
        /// Faculty with their ceiling and the load already assigned in the active semester, so
        /// the page can show a ceiling against what it has to accommodate.
        /// </summary>
        private static async Task<List<object>> LoadFacultyAsync(AppDbContext db, CancellationToken ct)
        {
            var activeSemesterId = await db.Semesters.AsNoTracking()
                .Where(s => s.IsActive)
                .Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync(ct);

            var assignedUnits = activeSemesterId is null
                ? new Dictionary<Guid, int>()
                : (await db.FacultyLoadAssignments.AsNoTracking()
                        .Where(l => l.SemesterId == activeSemesterId)
                        .Select(l => new { l.FacultyProfileId, Units = l.Subject!.Units })
                        .ToListAsync(ct))
                    .GroupBy(x => x.FacultyProfileId)
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Units));

            return (await db.FacultyProfiles.AsNoTracking()
                    .Include(f => f.User)
                    .OrderBy(f => f.User!.LastName).ThenBy(f => f.User!.FirstName)
                    .ToListAsync(ct))
                .Select(f => (object)new
                {
                    id = f.Id,
                    name = f.User?.FullName ?? "(unknown)",
                    employeeId = f.EmployeeId,
                    programCode = f.ProgramCode,
                    maxLoadUnits = f.MaxLoadUnits,
                    assignedUnits = assignedUnits.GetValueOrDefault(f.Id, 0),
                    isActive = f.User?.IsActive ?? false
                })
                .ToList();
        }

        // ---------- Section capacity cap ----------

        private static async Task<IResult> SetSectionCapacityAsync(
            SectionCapacityRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            if (request.Cap is not { } cap || cap < 1 || cap > MaxSectionCapacityCap)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["cap"] = [$"The seat cap must be between 1 and {MaxSectionCapacityCap}."]
                });
            }

            var settings = await db.GetSettingsForUpdateAsync(ct);
            var previous = settings.SectionCapacityCap;
            if (previous == cap)
            {
                return Results.Ok(new { cap, sectionsAboveCap = await db.Sections.CountAsync(s => s.Capacity > cap, ct) });
            }

            settings.SectionCapacityCap = cap;
            settings.UpdatedAtUtc = DateTime.UtcNow;

            // Deliberately not rewriting existing sections: a section whose seats are already
            // filled cannot be lowered below its EnrolledCount without violating
            // CK_Sections_EnrolledCount. The cap governs new sections and future edits; the
            // count of sections left above it is returned so the admin can act on them.
            var sectionsAboveCap = await db.Sections.CountAsync(s => s.Capacity > cap, ct);

            audit.Record(AuditAction.SectionCapacityCapChanged,
                $"Changed the section seat cap from {previous} to {cap}." +
                (sectionsAboveCap > 0
                    ? $" {sectionsAboveCap} existing section(s) remain above the new cap and were left unchanged."
                    : string.Empty),
                "SystemSettings", SystemSettings.SingletonId.ToString());
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { cap, sectionsAboveCap });
        }

        // ---------- Allowable time slots ----------

        private static async Task<IResult> CreateTimeSlotAsync(
            TimeSlotRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var (ok, day, start, end, problem) = await ValidateSlotAsync(request, null, db, ct);
            if (!ok) return problem;

            var slot = new TimeSlot { Day = day, StartMinutes = start, EndMinutes = end };
            db.TimeSlots.Add(slot);
            audit.Record(AuditAction.TimeSlotSaved,
                $"Added the allowable time slot {day} {Hhmm(start)}–{Hhmm(end)}.",
                "TimeSlot", slot.Id.ToString());
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/parameters/time-slots/{slot.Id}", new
            {
                id = slot.Id,
                day = (int)slot.Day,
                startMinutes = slot.StartMinutes,
                endMinutes = slot.EndMinutes,
                inUse = false
            });
        }

        private static async Task<IResult> UpdateTimeSlotAsync(
            Guid id, TimeSlotRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var slot = await db.TimeSlots.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (slot is null) return Results.NotFound(new { message = "Time slot not found." });

            // Moving a slot would silently move every class already placed in it, so an in-use
            // slot is immutable — the admin adds the replacement and reschedules deliberately.
            if (await db.ScheduleAssignments.AnyAsync(a => a.TimeSlotId == id, ct))
            {
                return Results.Conflict(new
                {
                    message = "This time slot is used in the schedule and can't be changed. Add a new slot instead."
                });
            }

            var (ok, day, start, end, problem) = await ValidateSlotAsync(request, id, db, ct);
            if (!ok) return problem;

            var before = $"{slot.Day} {Hhmm(slot.StartMinutes)}–{Hhmm(slot.EndMinutes)}";
            slot.Day = day;
            slot.StartMinutes = start;
            slot.EndMinutes = end;

            audit.Record(AuditAction.TimeSlotSaved,
                $"Changed the allowable time slot {before} to {day} {Hhmm(start)}–{Hhmm(end)}.",
                "TimeSlot", slot.Id.ToString());
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                id = slot.Id,
                day = (int)slot.Day,
                startMinutes = slot.StartMinutes,
                endMinutes = slot.EndMinutes,
                inUse = false
            });
        }

        private static async Task<IResult> DeleteTimeSlotAsync(
            Guid id, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var slot = await db.TimeSlots.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (slot is null) return Results.NotFound(new { message = "Time slot not found." });

            // Same reasoning as rooms: never orphan a placed class (FR-SCHED-02).
            if (await db.ScheduleAssignments.AnyAsync(a => a.TimeSlotId == id, ct))
            {
                return Results.Conflict(new
                {
                    message = "This time slot is used in the schedule and can't be deleted."
                });
            }

            var label = $"{slot.Day} {Hhmm(slot.StartMinutes)}–{Hhmm(slot.EndMinutes)}";
            db.TimeSlots.Remove(slot);
            audit.Record(AuditAction.TimeSlotSaved, $"Removed the allowable time slot {label}.",
                "TimeSlot", slot.Id.ToString());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }

        private static async Task<(bool Ok, DayOfWeek Day, int Start, int End, IResult Problem)>
            ValidateSlotAsync(TimeSlotRequest request, Guid? excludeId, AppDbContext db, CancellationToken ct)
        {
            var errors = new Dictionary<string, string[]>();

            if (request.Day is not { } rawDay || rawDay < 0 || rawDay > 6)
            {
                errors["day"] = ["Choose a day of the week."];
                rawDay = 0;
            }

            var start = request.StartMinutes ?? -1;
            var end = request.EndMinutes ?? -1;

            if (start < 0 || start >= 24 * 60) errors["startMinutes"] = ["Choose a start time."];
            if (end < 0 || end > 24 * 60) errors["endMinutes"] = ["Choose an end time."];
            if (errors.Count == 0 && end <= start)
            {
                errors["endMinutes"] = ["The end time must be after the start time."];
            }

            if (errors.Count == 0)
            {
                var day = (DayOfWeek)rawDay;
                var duplicate = await db.TimeSlots.AnyAsync(t =>
                    t.IsAllowable && t.Day == day && t.StartMinutes == start && t.EndMinutes == end
                    && (excludeId == null || t.Id != excludeId), ct);
                if (duplicate)
                {
                    errors["startMinutes"] = ["That exact slot already exists for this day."];
                }
            }

            if (errors.Count > 0)
            {
                return (false, DayOfWeek.Monday, 0, 0, Results.ValidationProblem(errors));
            }

            return (true, (DayOfWeek)rawDay, start, end, Results.Empty);
        }

        // ---------- Faculty unit-load ceilings ----------

        private static async Task<IResult> SetLoadLimitAsync(
            Guid id, LoadLimitRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var faculty = await db.FacultyProfiles.Include(f => f.User)
                .FirstOrDefaultAsync(f => f.Id == id, ct);
            if (faculty is null) return Results.NotFound(new { message = "Faculty member not found." });

            if (request.MaxLoadUnits is not { } units || units < 1 || units > MaxLoadUnitsCeiling)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["maxLoadUnits"] = [$"The ceiling must be between 1 and {MaxLoadUnitsCeiling} units."]
                });
            }

            // FR-FAC-03 is enforced when a load is saved, so a ceiling dropped below the load
            // already assigned would leave that faculty over the line with no way to save their
            // load again. Refuse, and say what to unassign first.
            var activeSemesterId = await db.Semesters.AsNoTracking()
                .Where(s => s.IsActive)
                .Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync(ct);

            var assigned = activeSemesterId is null
                ? 0
                : await db.FacultyLoadAssignments.AsNoTracking()
                    .Where(l => l.FacultyProfileId == id && l.SemesterId == activeSemesterId)
                    .SumAsync(l => l.Subject!.Units, ct);

            if (units < assigned)
            {
                return Results.Conflict(new
                {
                    message = $"{faculty.User?.FullName} already has {assigned} units assigned this semester. " +
                              $"Lower their load below {units} units first, then set the ceiling."
                });
            }

            var previous = faculty.MaxLoadUnits;
            if (previous != units)
            {
                faculty.MaxLoadUnits = units;
                audit.Record(AuditAction.FacultyLoadLimitChanged,
                    $"Changed {faculty.User?.FullName}'s unit-load ceiling from {previous} to {units}.",
                    "FacultyProfile", faculty.Id.ToString());
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new { id = faculty.Id, maxLoadUnits = units, assignedUnits = assigned });
        }

        private static string Hhmm(int minutes) => $"{minutes / 60:D2}:{minutes % 60:D2}";
    }
}
