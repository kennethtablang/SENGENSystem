using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Scheduling.Engine;

namespace SENGENSystem.Server.Features.Scheduling.GenerateSchedule
{
    // Vertical slice: the Academic Head triggers CSP schedule generation for a semester
    // and reviews the result before publishing (FR-SCHED-01/06, FR-FAC-04).
    public record GenerateScheduleRequest(Guid? SemesterId);

    public record GenerateScheduleResponse(
        Guid SemesterId,
        string SemesterName,
        int SectionCount,
        int AssignedCount,
        int Steps,
        IReadOnlyList<ScheduleRowDto> Schedule);

    public static class GenerateScheduleEndpoint
    {
        public static IEndpointRouteBuilder MapGenerateSchedule(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/scheduling/generate", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.AcademicHead)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            GenerateScheduleRequest request,
            AppDbContext db,
            CspScheduler scheduler,
            AuditLog audit,
            Features.Reports.Live.ReportsBroadcaster broadcaster,
            CancellationToken cancellationToken)
        {
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

            var sections = await db.Sections
                .Include(s => s.Subject)
                .Where(s => s.SemesterId == semester.Id)
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

            var rooms = await db.Rooms.ToListAsync(cancellationToken);
            var timeSlots = await db.TimeSlots.ToListAsync(cancellationToken);
            var faculty = await db.FacultyProfiles.ToListAsync(cancellationToken);

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

            var problem = new ScheduleProblem
            {
                Sections = sections.Select(s => new SectionVar(
                    s.Id,
                    s.SectionCode,
                    s.ProgramCode,
                    s.CohortKey,
                    s.Capacity,
                    s.Subject?.Units ?? 0,
                    s.Subject?.RequiresLaboratory ?? false)).ToList(),
                Rooms = rooms.Select(r => new RoomOption(r.Id, r.Capacity, r.IsLaboratory)).ToList(),
                TimeSlots = timeSlots,
                Faculty = faculty.Select(f => new FacultyOption(
                    f.Id, f.ProgramCode, f.MaxLoadUnits, preferences.GetValueOrDefault(f.Id))).ToList()
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
                // An engine bug must never bubble up as a bare 500 with no context.
                audit.Record(AuditAction.ScheduleGenerationFailed,
                    $"Schedule generation for {semester.Name} crashed: {ex.Message}",
                    "Semester", semester.Id.ToString());
                await db.SaveChangesAsync(cancellationToken);
                return Results.Problem(
                    title: "Schedule generation failed unexpectedly.",
                    detail: "The scheduling engine hit an internal error. The attempt was recorded in the audit trail — try again, and report this if it persists.",
                    statusCode: StatusCodes.Status500InternalServerError);
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

            foreach (var a in result.Assignments)
            {
                db.ScheduleAssignments.Add(new ScheduleAssignment
                {
                    SemesterId = semester.Id,
                    SectionId = a.SectionId,
                    RoomId = a.RoomId,
                    TimeSlotId = a.TimeSlotId,
                    FacultyProfileId = a.FacultyProfileId
                });
            }

            audit.Record(AuditAction.ScheduleGenerated,
                $"Generated a conflict-free schedule for {semester.Name}: " +
                $"{result.Assignments.Count} of {sections.Count} sections placed in {result.Steps:N0} search steps.",
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
                schedule));
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
