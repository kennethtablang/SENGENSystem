using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Registration.TransfereeEvaluation
{
    // Vertical slice: the Registrar's credit evaluation of a transferee (FR-EVAL-01/02). A
    // transferee arrives carrying subjects passed somewhere else; this is where someone rules,
    // subject by subject against the curriculum they are entering, which of those count here.
    //
    // The ruling settles two things a transferee cannot enlist without: the subjects they still
    // have to take, and — from the units credited — the year level they enter at. So a completed
    // evaluation is a hard gate on enlistment (EnlistmentEligibility), placed before it in the
    // student's journey exactly as document clearance is for a new enrollee.
    public static class TransfereeEvaluationEndpoints
    {
        public static IEndpointRouteBuilder MapTransfereeEvaluation(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/transferee-evaluations")
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.Registrar), nameof(UserRole.AdmissionOfficer), nameof(UserRole.SchoolAdmin)));

            group.MapGet("", ListAsync);
            group.MapGet("{registrationId:guid}", GetSheetAsync);
            group.MapPut("{registrationId:guid}", SaveAsync);
            group.MapPost("{registrationId:guid}/complete", CompleteAsync);
            group.MapPost("{registrationId:guid}/reopen", ReopenAsync);
            return app;
        }

        // GET /api/transferee-evaluations — the queue. Every transferee registration, with how far
        // their evaluation has got, so "who is waiting on me?" is the first thing the page answers.
        private static async Task<IResult> ListAsync(
            string? status, string? search, AppDbContext db, CancellationToken ct)
        {
            var query = db.StudentRegistrations.AsNoTracking()
                .Where(r => r.StudentType == StudentType.Transferee
                    && r.Status != RegistrationStatus.Rejected)
                .Include(r => r.Semester)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(r =>
                    r.StudentNumber.Contains(term)
                    || r.LastName.Contains(term)
                    || r.FirstName.Contains(term)
                    || (r.OfficialStudentNumber != null && r.OfficialStudentNumber.Contains(term)));
            }

            var registrations = await query
                .OrderByDescending(r => r.CreatedAtUtc)
                .Take(500)
                .ToListAsync(ct);

            var ids = registrations.Select(r => r.Id).ToList();
            var evaluations = await db.TransfereeEvaluations.AsNoTracking()
                .Where(e => ids.Contains(e.StudentRegistrationId))
                .Include(e => e.Items).ThenInclude(i => i.Subject)
                .ToListAsync(ct);
            var byRegistration = evaluations.ToDictionary(e => e.StudentRegistrationId);

            var rows = registrations.Select(r =>
            {
                byRegistration.TryGetValue(r.Id, out var evaluation);
                var totals = Totals(evaluation);
                return new EvaluationQueueRowDto(
                    r.Id,
                    r.StudentNumber,
                    r.OfficialStudentNumber,
                    r.FullName,
                    r.Program.ToString(),
                    r.Status.ToString(),
                    r.Semester?.Name,
                    (evaluation?.Status ?? TransfereeEvaluationStatus.Pending).ToString(),
                    totals.Credited,
                    totals.ToTake,
                    totals.Undecided,
                    r.YearLevel,
                    Iso(evaluation?.EvaluatedAtUtc));
            });

            if (!string.IsNullOrWhiteSpace(status)
                && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
            {
                rows = rows.Where(r => string.Equals(r.Status, status, StringComparison.OrdinalIgnoreCase));
            }

            var list = rows.ToList();
            return Results.Ok(new
            {
                count = list.Count,
                pendingCount = list.Count(r => r.Status != nameof(TransfereeEvaluationStatus.Completed)),
                completedCount = list.Count(r => r.Status == nameof(TransfereeEvaluationStatus.Completed)),
                evaluations = list
            });
        }

        // GET /api/transferee-evaluations/{registrationId} — the sheet: every subject in the
        // student's curriculum, in curriculum order, carrying whatever verdict is on file.
        private static async Task<IResult> GetSheetAsync(
            Guid registrationId, AppDbContext db, CancellationToken ct)
        {
            var registration = await db.StudentRegistrations.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == registrationId, ct);
            if (registration is null)
            {
                return Results.NotFound(new { message = "Registration not found." });
            }
            if (registration.StudentType != StudentType.Transferee)
            {
                return Results.BadRequest(new
                {
                    message = $"{registration.FullName} is a new student — only transferees are credit-evaluated."
                });
            }

            var evaluation = await LoadEvaluationAsync(db, registrationId, tracking: false, ct);
            var curriculum = await ResolveCurriculumAsync(db, registration, evaluation, ct);
            var subjects = await CurriculumSubjectsAsync(db, curriculum, ct);

            return Results.Ok(BuildSheet(registration, evaluation, curriculum, subjects));
        }

        // PUT /api/transferee-evaluations/{registrationId} — record decisions. Saving is
        // incremental: the Registrar works through a long curriculum over more than one sitting,
        // so a partial sheet is a first-class state (InProgress), not an error.
        private static async Task<IResult> SaveAsync(
            Guid registrationId,
            SaveEvaluationRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            CancellationToken ct)
        {
            var registration = await db.StudentRegistrations
                .FirstOrDefaultAsync(r => r.Id == registrationId, ct);
            if (registration is null)
            {
                return Results.NotFound(new { message = "Registration not found." });
            }
            if (registration.StudentType != StudentType.Transferee)
            {
                return Results.BadRequest(new
                {
                    message = $"{registration.FullName} is a new student — only transferees are credit-evaluated."
                });
            }

            var evaluation = await LoadEvaluationAsync(db, registrationId, tracking: true, ct);
            var curriculum = await ResolveCurriculumAsync(db, registration, evaluation, ct);
            var subjects = await CurriculumSubjectsAsync(db, curriculum, ct);
            var subjectIds = subjects.Select(s => s.Id).ToHashSet();

            if (evaluation is null)
            {
                evaluation = new Domain.TransfereeEvaluation
                {
                    StudentRegistrationId = registration.Id,
                    CurriculumId = curriculum?.Id
                };
                db.TransfereeEvaluations.Add(evaluation);
            }
            // A sheet ruled against a different catalog than the one now in force would be
            // misleading — re-pin it while the evaluation is still open.
            if (evaluation.Status != TransfereeEvaluationStatus.Completed && curriculum is not null)
            {
                evaluation.CurriculumId = curriculum.Id;
            }

            foreach (var incoming in request.Items ?? [])
            {
                if (!subjectIds.Contains(incoming.SubjectId))
                {
                    return Results.BadRequest(new
                    {
                        message = "One of the subjects is not part of this student's curriculum. Reload the sheet."
                    });
                }
                if (!Enum.TryParse<SubjectCreditDecision>(incoming.Decision, ignoreCase: true, out var decision)
                    || !Enum.IsDefined(decision))
                {
                    return Results.BadRequest(new { message = "Choose a valid decision for every subject." });
                }

                var item = evaluation.Items.FirstOrDefault(i => i.SubjectId == incoming.SubjectId);
                if (item is null)
                {
                    item = new TransfereeEvaluationItem { SubjectId = incoming.SubjectId };
                    evaluation.Items.Add(item);
                }
                item.Decision = decision;
                // Provenance only means something on a credit; clear it when the verdict changes,
                // so a "to take" row can't keep a stale source subject hanging off it.
                item.SourceSubject = decision == SubjectCreditDecision.Credited
                    ? Trimmed(incoming.SourceSubject) : null;
                item.SourceGrade = decision == SubjectCreditDecision.Credited
                    ? Trimmed(incoming.SourceGrade) : null;
            }

            if (request.Remarks is not null)
            {
                evaluation.Remarks = Trimmed(request.Remarks);
            }
            if (evaluation.Status == TransfereeEvaluationStatus.Pending && evaluation.Items.Count > 0)
            {
                evaluation.Status = TransfereeEvaluationStatus.InProgress;
            }
            evaluation.UpdatedAtUtc = DateTime.UtcNow;

            var totals = Totals(evaluation);
            audit.Record(AuditAction.TransfereeEvaluationSaved,
                $"Saved the credit evaluation of {registration.FullName} ({registration.StudentNumber}) — " +
                $"{totals.Credited} units credited, {totals.ToTake} to take.",
                "StudentRegistration", registration.Id.ToString());
            await db.SaveChangesAsync(ct);

            var saved = await LoadEvaluationAsync(db, registrationId, tracking: false, ct);
            return Results.Ok(BuildSheet(registration, saved, curriculum, subjects));
        }

        // POST /api/transferee-evaluations/{registrationId}/complete — sign off. This is the moment
        // the student's year level is set and their enlistment gate opens.
        private static async Task<IResult> CompleteAsync(
            Guid registrationId,
            CompleteEvaluationRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            Notifier notifier,
            CancellationToken ct)
        {
            var registration = await db.StudentRegistrations
                .FirstOrDefaultAsync(r => r.Id == registrationId, ct);
            if (registration is null)
            {
                return Results.NotFound(new { message = "Registration not found." });
            }

            var evaluation = await LoadEvaluationAsync(db, registrationId, tracking: true, ct);
            if (evaluation is null)
            {
                return Results.BadRequest(new
                {
                    message = "Record the credit decisions before completing this evaluation."
                });
            }

            var curriculum = await ResolveCurriculumAsync(db, registration, evaluation, ct);
            var subjects = await CurriculumSubjectsAsync(db, curriculum, ct);

            // Every subject must have a verdict. A sheet with gaps cannot answer "which subjects
            // are yours to take", which is the whole point of completing it.
            var decided = evaluation.Items
                .Where(i => i.Decision != SubjectCreditDecision.Undecided)
                .Select(i => i.SubjectId)
                .ToHashSet();
            var undecided = subjects.Where(s => !decided.Contains(s.Id)).ToList();
            if (undecided.Count > 0)
            {
                return Results.BadRequest(new
                {
                    message = $"{undecided.Count} subject(s) still have no decision. "
                        + "Mark every subject Credited or To take before completing.",
                    reasons = undecided.Take(10).Select(s => $"{s.Code} — {s.Title}").ToList()
                });
            }

            var totals = Totals(evaluation);
            var recommended = YearLevelPolicy.FromCreditedUnits(totals.Credited, UnitsByYear(subjects));
            var assigned = request.AssignedYearLevel is { } chosen ? chosen : recommended;
            if (!YearLevelPolicy.IsValid(assigned))
            {
                return Results.BadRequest(new
                {
                    message = $"Year level must be between {YearLevelPolicy.MinYearLevel} and {YearLevelPolicy.MaxYearLevel}."
                });
            }

            Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
            var registrarId = userId == Guid.Empty ? (Guid?)null : userId;

            evaluation.Status = TransfereeEvaluationStatus.Completed;
            evaluation.RecommendedYearLevel = recommended;
            evaluation.AssignedYearLevel = assigned;
            evaluation.EvaluatedByUserId = registrarId;
            evaluation.EvaluatedAtUtc = DateTime.UtcNow;
            evaluation.UpdatedAtUtc = DateTime.UtcNow;
            if (request.Remarks is not null) evaluation.Remarks = Trimmed(request.Remarks);

            var previousYear = registration.YearLevel;
            registration.YearLevel = assigned;
            registration.YearLevelSetAtUtc = DateTime.UtcNow;
            registration.YearLevelSetByUserId = registrarId;

            audit.Record(AuditAction.TransfereeEvaluationCompleted,
                $"Completed the credit evaluation of {registration.FullName} ({registration.StudentNumber}) — " +
                $"{totals.Credited} units credited, {totals.ToTake} to take, entering as " +
                $"{YearLevelPolicy.Label(assigned)}" +
                (assigned == recommended
                    ? "."
                    : $" (overriding the derived {YearLevelPolicy.Label(recommended)})."),
                "StudentRegistration", registration.Id.ToString());
            if (previousYear != assigned)
            {
                audit.Record(AuditAction.YearLevelAssigned,
                    $"Set {registration.FullName} ({registration.StudentNumber}) to {YearLevelPolicy.Label(assigned)} " +
                    $"from the completed transferee evaluation.",
                    "StudentRegistration", registration.Id.ToString());
            }

            if (registration.UserId is { } studentUserId)
            {
                notifier.Notify(studentUserId, NotificationKind.TransfereeEvaluation,
                    "Your credit evaluation is complete",
                    $"The Registrar credited {totals.Credited} units from your previous school. You are entering as "
                    + $"{YearLevelPolicy.Label(assigned)} with {totals.ToTake} units to take. You can now enlist in subjects.",
                    "/enlistment");
            }

            await db.SaveChangesAsync(ct);

            var saved = await LoadEvaluationAsync(db, registrationId, tracking: false, ct);
            return Results.Ok(BuildSheet(registration, saved, curriculum, subjects));
        }

        // POST /api/transferee-evaluations/{registrationId}/reopen — correct a signed-off sheet.
        // The decisions are kept: a correction is a revision, not a fresh start.
        private static async Task<IResult> ReopenAsync(
            Guid registrationId, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var registration = await db.StudentRegistrations
                .FirstOrDefaultAsync(r => r.Id == registrationId, ct);
            if (registration is null)
            {
                return Results.NotFound(new { message = "Registration not found." });
            }

            var evaluation = await LoadEvaluationAsync(db, registrationId, tracking: true, ct);
            if (evaluation is null || evaluation.Status != TransfereeEvaluationStatus.Completed)
            {
                return Results.Conflict(new { message = "This evaluation is not completed." });
            }

            evaluation.Status = TransfereeEvaluationStatus.InProgress;
            evaluation.EvaluatedAtUtc = null;
            evaluation.UpdatedAtUtc = DateTime.UtcNow;

            audit.Record(AuditAction.TransfereeEvaluationReopened,
                $"Reopened the credit evaluation of {registration.FullName} ({registration.StudentNumber}) — "
                + "the student cannot enlist until it is completed again.",
                "StudentRegistration", registration.Id.ToString());
            await db.SaveChangesAsync(ct);

            var curriculum = await ResolveCurriculumAsync(db, registration, evaluation, ct);
            var subjects = await CurriculumSubjectsAsync(db, curriculum, ct);
            var saved = await LoadEvaluationAsync(db, registrationId, tracking: false, ct);
            return Results.Ok(BuildSheet(registration, saved, curriculum, subjects));
        }

        // ---- shared helpers, also used by the PDF and the enlistment gate ----

        internal static async Task<Domain.TransfereeEvaluation?> LoadEvaluationAsync(
            AppDbContext db, Guid registrationId, bool tracking, CancellationToken ct)
        {
            var query = tracking
                ? db.TransfereeEvaluations.AsTracking()
                : db.TransfereeEvaluations.AsNoTracking();
            return await query
                .Include(e => e.Items).ThenInclude(i => i.Subject)
                .FirstOrDefaultAsync(e => e.StudentRegistrationId == registrationId, ct);
        }

        /// <summary>
        /// The curriculum an evaluation is read against: the one it was pinned to if it has one
        /// (so a signed sheet never silently re-reads against a newer catalog), otherwise the
        /// active curriculum for the student's program.
        /// </summary>
        internal static async Task<Domain.Curriculum?> ResolveCurriculumAsync(
            AppDbContext db, StudentRegistration registration, Domain.TransfereeEvaluation? evaluation, CancellationToken ct)
        {
            if (evaluation?.CurriculumId is { } pinned)
            {
                var existing = await db.Curricula.AsNoTracking().FirstOrDefaultAsync(c => c.Id == pinned, ct);
                if (existing is not null) return existing;
            }

            var program = registration.Program.ToString();
            return await db.Curricula.AsNoTracking()
                .Where(c => !c.IsArchived && c.ProgramCode == program)
                .OrderByDescending(c => c.IsActive)
                .FirstOrDefaultAsync(ct)
                ?? await db.Curricula.AsNoTracking()
                    .Where(c => !c.IsArchived)
                    .OrderByDescending(c => c.IsActive)
                    .FirstOrDefaultAsync(ct);
        }

        /// <summary>Every live subject in a curriculum, in the order a prospectus prints them.</summary>
        internal static async Task<List<Subject>> CurriculumSubjectsAsync(
            AppDbContext db, Domain.Curriculum? curriculum, CancellationToken ct)
        {
            if (curriculum is null) return [];
            return await db.Subjects.AsNoTracking()
                .Where(s => s.CurriculumId == curriculum.Id && !s.IsArchived)
                .OrderBy(s => s.YearLevel).ThenBy(s => s.Term).ThenBy(s => s.Code)
                .ToListAsync(ct);
        }

        /// <summary>Units offered per year level — the ladder the year-level derivation measures against.</summary>
        internal static Dictionary<int, int> UnitsByYear(IEnumerable<Subject> subjects) =>
            subjects.GroupBy(s => s.YearLevel).ToDictionary(g => g.Key, g => g.Sum(s => s.Units));

        internal static (int Credited, int ToTake, int Undecided) Totals(Domain.TransfereeEvaluation? evaluation)
        {
            if (evaluation is null) return (0, 0, 0);
            var credited = evaluation.Items
                .Where(i => i.Decision == SubjectCreditDecision.Credited)
                .Sum(i => i.Subject?.Units ?? 0);
            var toTake = evaluation.Items
                .Where(i => i.Decision == SubjectCreditDecision.ToTake)
                .Sum(i => i.Subject?.Units ?? 0);
            var undecided = evaluation.Items.Count(i => i.Decision == SubjectCreditDecision.Undecided);
            return (credited, toTake, undecided);
        }

        /// <summary>Whether this student is cleared to enlist as far as credit evaluation goes.</summary>
        internal static bool IsCleared(Domain.TransfereeEvaluation? evaluation) =>
            evaluation?.Status == TransfereeEvaluationStatus.Completed;

        private static EvaluationSheetDto BuildSheet(
            StudentRegistration registration,
            Domain.TransfereeEvaluation? evaluation,
            Domain.Curriculum? curriculum,
            List<Subject> subjects)
        {
            var bySubject = evaluation?.Items.ToDictionary(i => i.SubjectId)
                ?? new Dictionary<Guid, TransfereeEvaluationItem>();

            var rows = subjects.Select(s =>
            {
                bySubject.TryGetValue(s.Id, out var item);
                return new EvaluationSubjectDto(
                    s.Id, s.Code, s.Title, s.Units, s.YearLevel,
                    s.Term.ToString(),
                    EvaluationMapping.TermLabel(s.Term),
                    (item?.Decision ?? SubjectCreditDecision.Undecided).ToString(),
                    item?.SourceSubject,
                    item?.SourceGrade,
                    []);
            }).ToList();

            var credited = subjects
                .Where(s => bySubject.TryGetValue(s.Id, out var i) && i.Decision == SubjectCreditDecision.Credited)
                .Sum(s => s.Units);
            var toTake = subjects
                .Where(s => bySubject.TryGetValue(s.Id, out var i) && i.Decision == SubjectCreditDecision.ToTake)
                .Sum(s => s.Units);
            var undecidedCount = subjects.Count(s =>
                !bySubject.TryGetValue(s.Id, out var i) || i.Decision == SubjectCreditDecision.Undecided);

            var recommended = YearLevelPolicy.FromCreditedUnits(credited, UnitsByYear(subjects));

            return new EvaluationSheetDto(
                registration.Id,
                registration.StudentNumber,
                registration.OfficialStudentNumber,
                registration.FullName,
                registration.Program.ToString(),
                registration.StudentType.ToString(),
                registration.Status.ToString(),
                registration.SchoolName,
                registration.SchoolProgram,
                curriculum?.Id,
                curriculum is null ? null : $"{curriculum.ProgramCode} — {curriculum.ProgramName}",
                (evaluation?.Status ?? TransfereeEvaluationStatus.Pending).ToString(),
                credited,
                toTake,
                subjects.Sum(s => s.Units),
                undecidedCount,
                recommended,
                evaluation?.AssignedYearLevel ?? recommended,
                registration.YearLevel,
                evaluation?.Remarks,
                Iso(evaluation?.EvaluatedAtUtc),
                rows);
        }

        private static string? Trimmed(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string? Iso(DateTime? value) =>
            value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc).ToString("o") : null;
    }
}
