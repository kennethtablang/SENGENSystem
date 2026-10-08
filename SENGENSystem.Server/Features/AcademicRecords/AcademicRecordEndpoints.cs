using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Paging;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Registration;

namespace SENGENSystem.Server.Features.AcademicRecords
{
    // Vertical slice: the Registrar's record of what each student has already taken and how it
    // ended (FR-ENL-01/06). This is the store three existing rules were quietly resting on nothing
    // without — prerequisite enforcement, repeat subjects in the enlistment plan, and a year level
    // that advances on what was earned rather than on the calendar. See AcademicHistory for how
    // they read it, and StudentSubjectRecord for why it holds verdicts and not grades.
    //
    // The queue is deliberately *not* scoped to the active term the way the transferee and document
    // queues are. A student's history spans their whole time at the school, and the backlog worth
    // seeing is "who has nothing on file at all" — which is mostly returning students whose
    // registration belongs to a term that ended long ago.

    public record AcademicRecordQueueRowDto(
        Guid RegistrationId,
        string StudentNumber,
        string? OfficialStudentNumber,
        string FullName,
        string Program,
        string StudentType,
        int YearLevel,
        int RecordCount,
        int PassedCount,
        int EarnedUnits);

    /// <summary>One attempt at a subject — a subject that was failed and retaken has two.</summary>
    public record AcademicRecordAttemptDto(
        Guid SemesterId,
        string SemesterName,
        string Verdict,
        string? Remarks,
        string? RecordedAtUtc);

    public record AcademicRecordSubjectDto(
        Guid SubjectId,
        string Code,
        string Title,
        int Units,
        int YearLevel,
        string Term,
        string TermLabel,
        // Passed · Owed (attempted, not passed) · Credited (transferee) · NotTaken
        string Status,
        IReadOnlyList<AcademicRecordAttemptDto> Attempts,
        IReadOnlyList<string> Prerequisites);

    public record AcademicRecordTermDto(Guid SemesterId, string Name, bool IsActive);

    public record AcademicRecordSheetDto(
        Guid RegistrationId,
        string StudentNumber,
        string? OfficialStudentNumber,
        string FullName,
        string Program,
        string StudentType,
        int YearLevel,
        Guid? CurriculumId,
        string? CurriculumName,
        int RecordCount,
        int EarnedUnits,
        int TotalUnits,
        int PassedCount,
        int OwedCount,
        int? DerivedYearLevel,
        bool IsEnforceable,
        IReadOnlyList<AcademicRecordTermDto> Terms,
        IReadOnlyList<AcademicRecordSubjectDto> Subjects);

    public record SaveAcademicRecordItem(Guid SubjectId, string Verdict, string? Remarks);

    public record SaveAcademicRecordRequest(Guid SemesterId, IReadOnlyList<SaveAcademicRecordItem>? Items);

    public static class AcademicRecordEndpoints
    {
        /// <summary>The sentinel a client sends to remove a verdict rather than change it.</summary>
        private const string NoVerdict = "None";

        public static IEndpointRouteBuilder MapAcademicRecords(this IEndpointRouteBuilder app)
        {
            // The Registrar owns the academic record. The Academic Head is here because the sheet is
            // read against their curriculum, and because they are who answers when a prerequisite
            // refuses a seat. The two admin roles arrive through the claims transformation.
            var group = app.MapGroup("/api/academic-records")
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.Registrar), nameof(UserRole.AcademicHead),
                    nameof(UserRole.SchoolAdmin)));

            group.MapGet("", ListAsync);
            group.MapGet("{registrationId:guid}", GetSheetAsync);
            group.MapPut("{registrationId:guid}", SaveAsync);
            group.MapPost("import", ImportAsync).DisableAntiforgery();
            group.MapGet("template", Template);
            return app;
        }

        // GET /api/academic-records — who has history on file and who has none.
        private static async Task<IResult> ListAsync(
            string? view, string? search, int? page, int? pageSize, string? sort, string? dir,
            AppDbContext db, CancellationToken ct)
        {
            var query = db.StudentRegistrations.AsNoTracking()
                .Where(r => r.Status != RegistrationStatus.Rejected)
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

            // The tallies are counted against this — before the view chip narrows it — so switching
            // from "All" to "No records" cannot change what the page reports the backlog to be.
            var baseQuery = query;

            // The filter runs in SQL for the same reason the sort does: applied after the fetch it
            // would filter one page while the total counted the rest.
            var recorded = db.StudentSubjectRecords.AsNoTracking();
            query = (view?.ToLowerInvariant()) switch
            {
                "recorded" => query.Where(r => recorded.Any(x => x.StudentRegistrationId == r.Id)),
                "none" => query.Where(r => !recorded.Any(x => x.StudentRegistrationId == r.Id)),
                _ => query
            };

            var desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            var ordered = (sort?.ToLowerInvariant()) switch
            {
                "studentnumber" => desc
                    ? query.OrderByDescending(r => r.OfficialStudentNumber ?? r.StudentNumber)
                    : query.OrderBy(r => r.OfficialStudentNumber ?? r.StudentNumber),
                "fullname" => desc
                    ? query.OrderByDescending(r => r.LastName).ThenByDescending(r => r.FirstName)
                    : query.OrderBy(r => r.LastName).ThenBy(r => r.FirstName),
                "program" => desc ? query.OrderByDescending(r => r.Program) : query.OrderBy(r => r.Program),
                "yearlevel" => desc ? query.OrderByDescending(r => r.YearLevel) : query.OrderBy(r => r.YearLevel),
                // The last two are properties of the records, not the registration, so they sort
                // through a subquery — a student with nothing on file sorts as 0, which is what
                // their row displays.
                "records" => desc
                    ? query.OrderByDescending(r => recorded.Count(x => x.StudentRegistrationId == r.Id))
                    : query.OrderBy(r => recorded.Count(x => x.StudentRegistrationId == r.Id)),
                "earnedunits" => desc
                    ? query.OrderByDescending(r => recorded
                        .Where(x => x.StudentRegistrationId == r.Id && x.Verdict == SubjectVerdict.Passed)
                        .Sum(x => x.Subject!.Units))
                    : query.OrderBy(r => recorded
                        .Where(x => x.StudentRegistrationId == r.Id && x.Verdict == SubjectVerdict.Passed)
                        .Sum(x => x.Subject!.Units)),
                _ => query.OrderBy(r => r.LastName).ThenBy(r => r.FirstName)
            };

            // A stable tie-break, without which two students with the same surname can swap places
            // between pages.
            var paged = await ordered.ThenBy(r => r.Id).ToPagedAsync(PageSpec.From(page, pageSize), ct);

            // The per-student tallies for this page only. Folding them into the projection above
            // would have cost the ordering (a Select is no longer an IOrderedQueryable, which is
            // exactly the compile error ToPagedAsync exists to raise), and one keyed lookup over 25
            // ids is cheaper than three correlated subqueries per row anyway.
            var ids = paged.Items.Select(r => r.Id).ToList();
            var tallies = (await db.StudentSubjectRecords.AsNoTracking()
                .Where(x => ids.Contains(x.StudentRegistrationId))
                .Select(x => new { x.StudentRegistrationId, x.Verdict, x.Subject!.Units })
                .ToListAsync(ct))
                .GroupBy(x => x.StudentRegistrationId)
                .ToDictionary(g => g.Key, g => (
                    Count: g.Count(),
                    Passed: g.Count(x => x.Verdict == SubjectVerdict.Passed),
                    Units: g.Where(x => x.Verdict == SubjectVerdict.Passed).Sum(x => x.Units)));

            var rows = paged.Select(r =>
            {
                var tally = tallies.GetValueOrDefault(r.Id);
                return new AcademicRecordQueueRowDto(
                    r.Id, r.StudentNumber, r.OfficialStudentNumber, r.FullName,
                    r.Program.ToString(), r.StudentType.ToString(), r.YearLevel,
                    tally.Count, tally.Passed, tally.Units);
            });

            var recordedCount = await baseQuery.CountAsync(
                r => recorded.Any(x => x.StudentRegistrationId == r.Id), ct);
            var totalInQueue = await baseQuery.CountAsync(ct);

            var body = rows.ToResponse("students");
            body["recordedCount"] = recordedCount;
            body["notRecordedCount"] = totalInQueue - recordedCount;
            return Results.Ok(body);
        }

        // GET /api/academic-records/{registrationId} — the sheet: the student's whole curriculum,
        // in prospectus order, carrying every attempt on file.
        private static async Task<IResult> GetSheetAsync(
            Guid registrationId, AppDbContext db, CancellationToken ct)
        {
            var registration = await db.StudentRegistrations.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == registrationId, ct);
            if (registration is null)
            {
                return Results.NotFound(new { message = "Registration not found." });
            }

            return Results.Ok(await BuildSheetAsync(db, registration, ct));
        }

        // PUT /api/academic-records/{registrationId} — record verdicts for one term. Saving is
        // per-term because that is how the paper record arrives: a class list for one semester.
        private static async Task<IResult> SaveAsync(
            Guid registrationId,
            SaveAcademicRecordRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            CancellationToken ct)
        {
            var registration = await db.StudentRegistrations.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == registrationId, ct);
            if (registration is null)
            {
                return Results.NotFound(new { message = "Registration not found." });
            }

            var semester = await db.Semesters.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == request.SemesterId, ct);
            if (semester is null)
            {
                return Results.BadRequest(new { message = "Pick the term these subjects were taken in." });
            }

            var curriculum = await ResolveCurriculumAsync(db, registration, ct);
            var subjectIds = curriculum is null
                ? new HashSet<Guid>()
                : (await db.Subjects.AsNoTracking()
                    .Where(s => s.CurriculumId == curriculum.Id)
                    .Select(s => s.Id)
                    .ToListAsync(ct)).ToHashSet();

            var existing = await db.StudentSubjectRecords
                .Where(r => r.StudentRegistrationId == registrationId && r.SemesterId == semester.Id)
                .ToListAsync(ct);

            Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
            var recordedBy = userId == Guid.Empty ? (Guid?)null : userId;

            var saved = 0;
            var removed = 0;
            foreach (var incoming in request.Items ?? [])
            {
                if (!subjectIds.Contains(incoming.SubjectId))
                {
                    return Results.BadRequest(new
                    {
                        message = "One of the subjects is not part of this student's curriculum. Reload the sheet."
                    });
                }

                var row = existing.FirstOrDefault(r => r.SubjectId == incoming.SubjectId);

                // "None" is how the sheet clears a verdict recorded by mistake. It removes only this
                // term's row, so clearing a retake cannot silently erase the original attempt.
                if (string.IsNullOrWhiteSpace(incoming.Verdict)
                    || string.Equals(incoming.Verdict, NoVerdict, StringComparison.OrdinalIgnoreCase))
                {
                    if (row is not null)
                    {
                        db.StudentSubjectRecords.Remove(row);
                        removed++;
                    }
                    continue;
                }

                if (!Enum.TryParse<SubjectVerdict>(incoming.Verdict, ignoreCase: true, out var verdict)
                    || !Enum.IsDefined(verdict))
                {
                    return Results.BadRequest(new { message = "Choose a valid verdict for every subject." });
                }

                if (row is null)
                {
                    row = new StudentSubjectRecord
                    {
                        StudentRegistrationId = registrationId,
                        SubjectId = incoming.SubjectId,
                        SemesterId = semester.Id,
                        RecordedByUserId = recordedBy
                    };
                    db.StudentSubjectRecords.Add(row);
                }
                else
                {
                    row.UpdatedAtUtc = DateTime.UtcNow;
                }
                row.Verdict = verdict;
                row.Remarks = string.IsNullOrWhiteSpace(incoming.Remarks) ? null : incoming.Remarks.Trim();
                saved++;
            }

            if (saved > 0 || removed > 0)
            {
                audit.Record(AuditAction.AcademicRecordSaved,
                    $"Recorded {saved} subject verdict(s) for {registration.FullName} " +
                    $"({registration.StudentNumber}) in {semester.Name}" +
                    (removed > 0 ? $", clearing {removed}." : "."),
                    "StudentRegistration", registrationId.ToString());
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(await BuildSheetAsync(db, registration, ct));
        }

        // POST /api/academic-records/import — the backfill path. A school arrives with terms of
        // history in a spreadsheet, and typing it in one sheet at a time is not a plan.
        private static async Task<IResult> ImportAsync(
            IFormFile? file,
            ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            CancellationToken ct)
        {
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { message = "Attach an .xlsx file to import." });
            }
            if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new
                {
                    message = "Only .xlsx workbooks are supported. Download the template to start."
                });
            }
            if (file.Length > XlsxAcademicRecordImporter.MaxFileBytes)
            {
                return Results.BadRequest(new { message = "The file is too large (5 MB max)." });
            }

            Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);

            AcademicRecordImportReport report;
            try
            {
                await using var stream = file.OpenReadStream();
                report = await XlsxAcademicRecordImporter.ImportAsync(
                    stream, db, userId == Guid.Empty ? null : userId, ct);
            }
            catch (FormatException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (Exception)
            {
                return Results.BadRequest(new
                {
                    message = "The file could not be read as an Excel workbook. Save it as .xlsx and try again."
                });
            }

            if (report.Loaded > 0 || report.Updated > 0)
            {
                audit.Record(AuditAction.AcademicRecordImported,
                    $"Imported academic records from \"{file.FileName}\" — {report.Loaded} added, " +
                    $"{report.Updated} updated, {report.Failed} row(s) failed validation.",
                    "StudentSubjectRecord", string.Empty);
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new
            {
                report.TotalRows,
                report.Loaded,
                report.Updated,
                report.Failed,
                rows = report.Rows
            });
        }

        private static async Task<IResult> Template(AppDbContext db, CancellationToken ct)
        {
            var termNames = await db.Semesters.AsNoTracking()
                .OrderByDescending(s => s.StartDate)
                .Select(s => s.Name)
                .ToListAsync(ct);
            return Results.File(
                XlsxAcademicRecordImporter.BuildTemplate(termNames),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "sengen-academic-records-template.xlsx");
        }

        // ---- shared ----

        private static async Task<AcademicRecordSheetDto> BuildSheetAsync(
            AppDbContext db, StudentRegistration registration, CancellationToken ct)
        {
            var curriculum = await ResolveCurriculumAsync(db, registration, ct);

            var subjects = curriculum is null
                ? []
                : await db.Subjects.AsNoTracking()
                    .Where(s => s.CurriculumId == curriculum.Id && !s.IsArchived)
                    .OrderBy(s => s.YearLevel).ThenBy(s => s.Term).ThenBy(s => s.Code)
                    .ToListAsync(ct);

            var records = await db.StudentSubjectRecords.AsNoTracking()
                .Where(r => r.StudentRegistrationId == registration.Id)
                .Include(r => r.Semester)
                .ToListAsync(ct);
            var attemptsBySubject = records
                .GroupBy(r => r.SubjectId)
                .ToDictionary(g => g.Key, g => g
                    .OrderBy(r => r.Semester?.StartDate ?? default)
                    .Select(r => new AcademicRecordAttemptDto(
                        r.SemesterId,
                        r.Semester?.Name ?? "—",
                        r.Verdict.ToString(),
                        r.Remarks,
                        Iso(r.UpdatedAtUtc ?? r.RecordedAtUtc)))
                    .ToList());

            // Read through the same helper the enlistment gate uses, so the sheet cannot show a
            // student as having met something the gate then refuses them for.
            var history = await AcademicHistory.LoadAsync(db, registration.Id, ct);

            var prerequisiteCodes = curriculum is null
                ? new Dictionary<Guid, List<string>>()
                : (await db.SubjectPrerequisites.AsNoTracking()
                    .Where(p => p.Subject!.CurriculumId == curriculum.Id)
                    .Select(p => new { p.SubjectId, Code = p.PrerequisiteSubject!.Code })
                    .ToListAsync(ct))
                    .GroupBy(x => x.SubjectId)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.Code).OrderBy(c => c).ToList());

            var rows = subjects.Select(s =>
            {
                attemptsBySubject.TryGetValue(s.Id, out var attempts);
                var status = history.HasPassed(s.Id)
                    // A subject with no attempt of its own that still reads as passed was credited
                    // by a transferee evaluation — worth saying so rather than showing a bare
                    // "Passed" with nothing behind it.
                    ? (attempts is null ? "Credited" : "Passed")
                    : history.OwedSubjectIds.Contains(s.Id) ? "Owed" : "NotTaken";

                return new AcademicRecordSubjectDto(
                    s.Id, s.Code, s.Title, s.Units, s.YearLevel,
                    s.Term.ToString(),
                    s.Term == SemesterTerm.SecondSemester ? "Second Semester" : "First Semester",
                    status,
                    attempts ?? [],
                    prerequisiteCodes.GetValueOrDefault(s.Id) ?? []);
            }).ToList();

            var terms = await db.Semesters.AsNoTracking()
                .OrderByDescending(s => s.StartDate)
                .Select(s => new AcademicRecordTermDto(s.Id, s.Name, s.IsActive))
                .ToListAsync(ct);

            return new AcademicRecordSheetDto(
                registration.Id,
                registration.StudentNumber,
                registration.OfficialStudentNumber,
                registration.FullName,
                registration.Program.ToString(),
                registration.StudentType.ToString(),
                registration.YearLevel,
                curriculum?.Id,
                curriculum is null ? null : $"{curriculum.ProgramCode} — {curriculum.ProgramName}",
                records.Count,
                history.EarnedUnits,
                subjects.Sum(s => s.Units),
                rows.Count(r => r.Status is "Passed" or "Credited"),
                rows.Count(r => r.Status == "Owed"),
                await history.DeriveYearLevelAsync(db, registration, ct),
                history.IsEnforceable,
                terms,
                rows);
        }

        /// <summary>
        /// The curriculum a student's sheet is read against — the active catalog for their program,
        /// or the one their transferee evaluation was pinned to. Same convention as the evaluation
        /// sheet and the enlistment plan, so all three show the same subject list.
        /// </summary>
        internal static async Task<Domain.Curriculum?> ResolveCurriculumAsync(
            AppDbContext db, StudentRegistration registration, CancellationToken ct)
        {
            var pinned = await db.TransfereeEvaluations.AsNoTracking()
                .Where(e => e.StudentRegistrationId == registration.Id && e.CurriculumId != null)
                .Select(e => e.Curriculum)
                .FirstOrDefaultAsync(ct);
            var program = registration.Program.ToString();
            if (pinned is not null && pinned.ProgramCode == program) return pinned;

            return await db.Curricula.AsNoTracking()
                .Where(c => !c.IsArchived && c.ProgramCode == program)
                .OrderByDescending(c => c.IsActive)
                .FirstOrDefaultAsync(ct);
        }

        private static string? Iso(DateTime? value) =>
            value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc).ToString("o") : null;
    }
}
