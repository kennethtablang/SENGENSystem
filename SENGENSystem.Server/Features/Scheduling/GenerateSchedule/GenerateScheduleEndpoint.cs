using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Scheduling.Engine;

namespace SENGENSystem.Server.Features.Scheduling.GenerateSchedule
{
    // Vertical slice: the Academic Head triggers CSP schedule generation for a semester
    // and reviews the result before publishing (FR-SCHED-01/06, FR-FAC-04).
    // Seed is optional: omit it for a fresh random arrangement each run, or pass a specific one
    // to reproduce a previous timetable exactly (the value is returned on every response).
    public record GenerateScheduleRequest(Guid? SemesterId, int? Seed = null);

    public record GenerateScheduleResponse(
        Guid SemesterId,
        string SemesterName,
        int SectionCount,
        int AssignedCount,
        int Steps,
        int Seed,
        IReadOnlyList<ScheduleRowDto> Schedule,
        OptimizationSummaryDto Optimization);

    /// <summary>
    /// What the run achieved on the soft constraints. Zero hard violations is guaranteed;
    /// this is how good the result is <i>within</i> that guarantee, so the Academic Head can
    /// judge the timetable rather than take it on trust (FR-DASH-03).
    /// </summary>
    public record OptimizationSummaryDto(
        int PreferencesHonored,
        int PreferencesApplicable,
        double PreferenceHonorRatePct,
        double CohortIdleHours,
        double FacultyIdleHours,
        double LoadSpread,
        int MinLoadUnits,
        int MaxLoadUnits,
        bool LoadLooksUneven,
        double AverageRoomFitPct);

    public static class GenerateScheduleEndpoint
    {
        public static IEndpointRouteBuilder MapGenerateSchedule(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/scheduling/generate", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.AcademicHead), nameof(UserRole.SchoolAdmin)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            GenerateScheduleRequest request,
            AppDbContext db,
            CspScheduler scheduler,
            AuditLog audit,
            Features.Reports.Live.ReportsBroadcaster broadcaster,
            ILoggerFactory loggerFactory,
            HttpContext http,
            CancellationToken cancellationToken)
        {
            var logger = loggerFactory.CreateLogger("ScheduleGeneration");
            var semester = request.SemesterId is { } id
                ? await db.Semesters.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                : await db.Semesters.FirstOrDefaultAsync(s => s.IsActive, cancellationToken);

            if (semester is null)
            {
                return Results.BadRequest(new { message = "No target semester found. Activate a semester or pass a valid semesterId." });
            }

            if (semester.IsArchived)
            {
                return Results.Conflict(new { message = $"“{semester.Name}” is archived — its schedule is read-only." });
            }

            // A finalized draft is a deliberate sign-off; regenerating would silently discard it.
            // Require an explicit reopen first (FR-SCHED-06).
            var finalizedDraftExists = await db.ScheduleAssignments
                .AnyAsync(a => a.SemesterId == semester.Id && a.IsFinalized && !a.IsPublished, cancellationToken);
            if (finalizedDraftExists)
            {
                return Results.Conflict(new
                {
                    message = $"The {semester.Name} schedule is finalized and locked. Reopen it before regenerating."
                });
            }

            // Ordering is not cosmetic here. The engine breaks scoring ties by input order, so
            // an unordered query would let SQL Server's row order decide the timetable — two
            // runs over identical data could differ, contradicting FR-SCHED-08. Every input the
            // engine sees is given a total order.
            var sections = await db.Sections
                .Include(s => s.Subject)
                .Where(s => s.SemesterId == semester.Id)
                .OrderBy(s => s.SectionCode).ThenBy(s => s.Id)
                .ToListAsync(cancellationToken);

            if (sections.Count == 0)
            {
                return Results.BadRequest(new { message = $"No sections are configured for {semester.Name}." });
            }

            // FR-SCHED-04: curriculum awareness — the offered sections must respect the
            // prerequisite/co-requisite structure before any timetable is attempted.
            var curriculumIssues = await ValidateCurriculumAsync(db, sections, cancellationToken);
            if (curriculumIssues.Count > 0)
            {
                return Results.UnprocessableEntity(new
                {
                    message = "The offered sections violate the curriculum's prerequisite/co-requisite structure.",
                    reasons = curriculumIssues,
                    steps = 0
                });
            }

            // Sections with a dangling subject would enter the engine with 0 units and no lab
            // flag — surface them instead of silently producing a wrong timetable.
            var orphaned = sections.Where(s => s.Subject is null).Select(s => s.SectionCode).ToList();
            if (orphaned.Count > 0)
            {
                return Results.UnprocessableEntity(new
                {
                    message = "Some sections reference a subject that no longer exists. Remove or fix them first.",
                    reasons = orphaned.Select(code => $"{code}: its subject was deleted.").ToList(),
                    steps = 0
                });
            }

            // Archived subjects left the curriculum — offering them this term is a data error
            // the Academic Head must resolve (drop the section or restore the subject).
            var retired = sections.Where(s => s.Subject!.IsArchived).ToList();
            if (retired.Count > 0)
            {
                return Results.UnprocessableEntity(new
                {
                    message = "Some sections offer archived subjects. Remove those sections or restore the subjects, then regenerate.",
                    reasons = retired
                        .Select(s => $"{s.SectionCode}: {s.Subject!.Code} is archived" +
                            (s.Subject.ArchiveReason is null ? "." : $" ({s.Subject.ArchiveReason})."))
                        .ToList(),
                    steps = 0
                });
            }

            var rooms = await db.Rooms
                .OrderBy(r => r.Name).ThenBy(r => r.Id)
                .ToListAsync(cancellationToken);
            var timeSlots = await db.TimeSlots
                .OrderBy(t => t.Day).ThenBy(t => t.StartMinutes).ThenBy(t => t.Id)
                .ToListAsync(cancellationToken);
            var faculty = await db.FacultyProfiles
                .Include(f => f.User)
                .OrderBy(f => f.EmployeeId).ThenBy(f => f.Id)
                .ToListAsync(cancellationToken);

            // Clear 400s for empty resource pools — friendlier than a doomed solver run.
            var missing = new List<string>();
            if (rooms.Count == 0) missing.Add("no rooms are configured (Academic setup → Rooms)");
            if (timeSlots.Count == 0) missing.Add("no time slots exist yet");
            if (faculty.Count == 0) missing.Add("no faculty profiles exist (User management)");
            if (missing.Count > 0)
            {
                return Results.BadRequest(new
                {
                    message = $"Cannot generate a schedule: {string.Join("; ", missing)}."
                });
            }

            // FR-SCHED-03: faculty time-slot preferences feed the engine's soft scoring.
            var preferences = (await db.FacultyTimePreferences.AsNoTracking().ToListAsync(cancellationToken))
                .GroupBy(p => p.FacultyProfileId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<PreferredWindow>)g
                        .Select(p => new PreferredWindow(p.Day, p.StartMinutes, p.EndMinutes))
                        .ToList());

            // H5 — faculty are only ever placed on subjects they were allocated. The engine does
            // not choose who teaches: it reads the Academic Head's load allocation (FR-FAC-01)
            // and places those decisions into rooms and time slots. A load assignment and a
            // section are linked through the cohort the subject is delivered to — Section
            // carries (SubjectId, ProgramCode, YearLevel, Block), ClassSection the matching
            // (ProgramCode, YearLevel, SectionName).
            var allocations = await db.FacultyLoadAssignments.AsNoTracking()
                .Where(l => l.SemesterId == semester.Id)
                .Include(l => l.ClassSection)
                .ToListAsync(cancellationToken);

            var facultyBySection = new Dictionary<Guid, Guid>();
            var unallocated = new List<string>();
            foreach (var s in sections)
            {
                var match = allocations.FirstOrDefault(l =>
                    l.SubjectId == s.SubjectId
                    && l.ClassSection != null
                    && string.Equals(l.ClassSection.ProgramCode, s.ProgramCode, StringComparison.OrdinalIgnoreCase)
                    && l.ClassSection.YearLevel == s.YearLevel
                    && string.Equals(l.ClassSection.SectionName, s.Block, StringComparison.OrdinalIgnoreCase));

                if (match is null)
                {
                    unallocated.Add(
                        $"{s.SectionCode}: no faculty member is allocated to teach {s.Subject!.Code} " +
                        $"to {s.ProgramCode} {s.YearLevel}-{s.Block}.");
                    continue;
                }

                facultyBySection[s.Id] = match.FacultyProfileId;
            }

            if (unallocated.Count > 0)
            {
                // Allocation is now a precondition of generation, not something the engine
                // improvises. Failing here — with the exact sections named — is far more
                // actionable than a dead end deep in the search.
                return Results.UnprocessableEntity(new
                {
                    message = "Every section needs a faculty allocation before a schedule can be generated. "
                        + "Assign the missing subjects on the Faculty load page, then regenerate.",
                    reasons = unallocated,
                    steps = 0
                });
            }

            // A fresh seed per run makes each generation a different valid arrangement; a caller
            // can pin one to reproduce an earlier timetable. Non-negative so it reads cleanly in
            // the audit trail and can be re-entered by hand.
            var seed = request.Seed ?? Random.Shared.Next(1, int.MaxValue);

            var problem = new ScheduleProblem
            {
                Seed = seed,
                Sections = sections.Select(s => new SectionVar(
                    s.Id,
                    s.SectionCode,
                    s.ProgramCode,
                    s.CohortKey,
                    s.Capacity,
                    s.Subject?.Units ?? 0,
                    s.Subject?.RequiresLaboratory ?? false,
                    facultyBySection[s.Id],
                    // Weekly contact hours drive how long a block the engine must reserve. Fall
                    // back to units (then a single period) for any subject predating Subject.Hours.
                    RequiredMinutes: (s.Subject!.Hours > 0
                        ? s.Subject.Hours
                        : Math.Max(s.Subject.Units, 1)) * 60)).ToList(),
                Rooms = rooms.Select(r => new RoomOption(r.Id, r.Capacity, r.IsLaboratory)).ToList(),
                TimeSlots = timeSlots,
                Faculty = faculty.Select(f => new FacultyOption(
                    f.Id,
                    f.ProgramCode,
                    f.MaxLoadUnits,
                    preferences.GetValueOrDefault(f.Id),
                    // Name + employee ID so a failure message identifies a person, not a GUID.
                    string.IsNullOrWhiteSpace(f.EmployeeId)
                        ? f.User?.FullName ?? "(unknown)"
                        : $"{f.User?.FullName ?? "(unknown)"} ({f.EmployeeId})")).ToList()
            };

            ScheduleGenerationResult result;
            try
            {
                // CPU-bound search — run off the request thread so the 20s worst case
                // doesn't pin a request-processing thread.
                result = await Task.Run(() => scheduler.Solve(problem), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw; // client went away — let the pipeline handle it
            }
            catch (Exception ex)
            {
                // An engine bug must never bubble up as a bare 500 with no context. Give whoever
                // hit this a lead they can act on: a trace id that ties the message they see to
                // the full stack trace in the logs, plus the exception's own summary. This is an
                // Academic-Head-only endpoint, so surfacing the exception type/message is a
                // reportable diagnostic, not a leak to the public.
                var traceId = http.TraceIdentifier;
                logger.LogError(ex,
                    "Schedule generation for {Semester} ({SemesterId}) crashed [trace {TraceId}]",
                    semester.Name, semester.Id, traceId);
                audit.Record(AuditAction.ScheduleGenerationFailed,
                    $"Schedule generation for {semester.Name} crashed [trace {traceId}]: " +
                    $"{ex.GetType().Name}: {ex.Message}",
                    "Semester", semester.Id.ToString());
                await db.SaveChangesAsync(cancellationToken);
                return Results.Problem(
                    title: "Schedule generation failed unexpectedly.",
                    detail: $"The scheduling engine hit an internal error ({ex.GetType().Name}: {ex.Message}). " +
                        $"Quote reference {traceId} when reporting this — it points to the full diagnostics in the server log.",
                    statusCode: StatusCodes.Status500InternalServerError,
                    extensions: new Dictionary<string, object?>
                    {
                        ["reference"] = traceId,
                        ["reasons"] = new[]
                        {
                            $"{ex.GetType().Name}: {ex.Message}",
                            $"Diagnostic reference: {traceId} (find the full stack trace in the server log by this id)."
                        }
                    });
            }

            if (!result.Success)
            {
                // 422: inputs are valid but no conflict-free schedule could be produced.
                audit.Record(AuditAction.ScheduleGenerationFailed,
                    $"Schedule generation for {semester.Name} found no conflict-free timetable after {result.Steps:N0} steps.",
                    "Semester", semester.Id.ToString());
                await db.SaveChangesAsync(cancellationToken);
                return Results.UnprocessableEntity(new
                {
                    message = $"Could not generate a conflict-free schedule for {semester.Name}.",
                    reasons = result.UnplacedReasons,
                    steps = result.Steps
                });
            }

            // Replace any previously generated-but-unpublished draft; never disturb published rows.
            var existingDraft = await db.ScheduleAssignments
                .Where(a => a.SemesterId == semester.Id && !a.IsPublished)
                .ToListAsync(cancellationToken);
            db.ScheduleAssignments.RemoveRange(existingDraft);

            // The engine returns each section as one contiguous block covering its weekly hours,
            // which may span several base periods (e.g. a 3-hour subject over two 90-minute slots).
            // The grid stores only atomic periods, so persist (find-or-create) a TimeSlot for the
            // whole block — the same on-demand period pattern the manual board uses. A cache keeps
            // blocks with identical (day, start, end) mapped to a single row.
            var slotCache = new Dictionary<(DayOfWeek, int, int), TimeSlot>();
            foreach (var t in timeSlots) slotCache.TryAdd((t.Day, t.StartMinutes, t.EndMinutes), t);

            TimeSlot ResolveSlot(TimeSlot block)
            {
                var key = (block.Day, block.StartMinutes, block.EndMinutes);
                if (slotCache.TryGetValue(key, out var existing)) return existing;
                var created = new TimeSlot { Day = block.Day, StartMinutes = block.StartMinutes, EndMinutes = block.EndMinutes };
                db.TimeSlots.Add(created);
                slotCache[key] = created;
                return created;
            }

            foreach (var a in result.Assignments)
            {
                var slot = ResolveSlot(a.Slot);
                db.ScheduleAssignments.Add(new ScheduleAssignment
                {
                    SemesterId = semester.Id,
                    SectionId = a.SectionId,
                    RoomId = a.RoomId,
                    TimeSlotId = slot.Id,
                    FacultyProfileId = a.FacultyProfileId
                });
            }

            var opt = result.Optimization;
            audit.Record(AuditAction.ScheduleGenerated,
                $"Generated a conflict-free schedule for {semester.Name} (seed {seed}): " +
                $"{result.Assignments.Count} of {sections.Count} sections placed in {result.Steps:N0} search steps " +
                $"({opt.PreferenceHonorRatePct}% of time preferences honored, " +
                $"{opt.CohortIdleHours}h cohort idle time).",
                "Semester", semester.Id.ToString());

            await db.SaveChangesAsync(cancellationToken);
            broadcaster.Announce("scheduling");

            var schedule = await LoadScheduleAsync(db, semester.Id, cancellationToken);

            return Results.Ok(new GenerateScheduleResponse(
                semester.Id,
                semester.Name,
                sections.Count,
                result.Assignments.Count,
                result.Steps,
                seed,
                schedule,
                new OptimizationSummaryDto(
                    opt.PreferencesHonored,
                    opt.PreferencesApplicable,
                    opt.PreferenceHonorRatePct,
                    opt.CohortIdleHours,
                    opt.FacultyIdleHours,
                    opt.LoadSpread,
                    opt.MinLoadUnits,
                    opt.MaxLoadUnits,
                    opt.LoadLooksUneven,
                    opt.AverageRoomFitPct)));
        }

        /// <summary>
        /// FR-SCHED-04: validates the semester's offerings against `SubjectPrerequisite` edges.
        /// A prerequisite placed *later* in the curriculum than its dependent is a sequencing
        /// error; a prerequisite in the *same* year/term is a co-requisite, so every cohort
        /// offered the dependent must be offered the co-requisite too.
        /// </summary>
        private static async Task<List<string>> ValidateCurriculumAsync(
            AppDbContext db, List<Section> sections, CancellationToken cancellationToken)
        {
            var issues = new List<string>();
            var edges = await db.SubjectPrerequisites.AsNoTracking()
                .Include(p => p.Subject)
                .Include(p => p.PrerequisiteSubject)
                .ToListAsync(cancellationToken);

            static int CurriculumOrder(Subject s) =>
                s.YearLevel * 2 + (s.Term == SemesterTerm.SecondSemester ? 1 : 0);
            static string Where(Subject s) =>
                $"Year {s.YearLevel} {(s.Term == SemesterTerm.SecondSemester ? "2nd" : "1st")} Sem";

            foreach (var edge in edges)
            {
                if (edge.Subject is null || edge.PrerequisiteSubject is null) continue;
                var dependent = edge.Subject;
                var prerequisite = edge.PrerequisiteSubject;

                if (CurriculumOrder(prerequisite) > CurriculumOrder(dependent))
                {
                    issues.Add(
                        $"{dependent.Code} requires {prerequisite.Code}, but the curriculum places " +
                        $"{prerequisite.Code} later ({Where(prerequisite)}) than {dependent.Code} ({Where(dependent)}).");
                    continue;
                }

                if (CurriculumOrder(prerequisite) == CurriculumOrder(dependent))
                {
                    // Same year/term — a co-requisite pair: each cohort taking the dependent
                    // this semester must be offered the co-requisite alongside it.
                    var dependentCohorts = sections
                        .Where(s => s.SubjectId == dependent.Id)
                        .Select(s => s.CohortKey);
                    var prerequisiteCohorts = sections
                        .Where(s => s.SubjectId == prerequisite.Id)
                        .Select(s => s.CohortKey)
                        .ToHashSet();
                    foreach (var cohort in dependentCohorts.Where(c => !prerequisiteCohorts.Contains(c)))
                    {
                        issues.Add(
                            $"Block {cohort} is offered {dependent.Code} but not its same-term " +
                            $"co-requisite {prerequisite.Code} — add a {prerequisite.Code} section for that block.");
                    }
                }
            }

            return issues;
        }

        private static async Task<List<ScheduleRowDto>> LoadScheduleAsync(
            AppDbContext db, Guid semesterId, CancellationToken cancellationToken)
        {
            var rows = await db.ScheduleAssignments
                .Where(a => a.SemesterId == semesterId)
                .Include(a => a.Section).ThenInclude(s => s!.Subject)
                .Include(a => a.Room)
                .Include(a => a.TimeSlot)
                .Include(a => a.FacultyProfile).ThenInclude(f => f!.User)
                .ToListAsync(cancellationToken);

            return rows
                .OrderBy(a => a.Section!.CohortKey)
                .ThenBy(a => a.TimeSlot!.Day)
                .ThenBy(a => a.TimeSlot!.StartMinutes)
                .Select(ScheduleRowDto.From)
                .ToList();
        }
    }
}
