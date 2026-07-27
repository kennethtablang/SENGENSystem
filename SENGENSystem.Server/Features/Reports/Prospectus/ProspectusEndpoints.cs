using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Registration;
using SENGENSystem.Server.Features.Registration.TransfereeEvaluation;

namespace SENGENSystem.Server.Features.Reports.Prospectus
{
    // Vertical slice: printable subject listings (FR-RPT-05). Three documents, one shared idea —
    // "what subjects does this person take?" answered at three different scopes:
    //   · the curriculum prospectus  — what a Year N student of a program takes (the generic sheet),
    //   · the transferee evaluation  — the same curriculum with one student's credits ruled on,
    //   · the certificate of registration — the subjects one student actually holds a seat in now.
    //
    // Staff reach all three. A student reaches their own two, and nobody else's: the student routes
    // resolve the registration from the signed-in account rather than taking an id, so there is no
    // id to tamper with.
    public static class ProspectusEndpoints
    {
        private const string Pdf = "application/pdf";

        public static IEndpointRouteBuilder MapProspectus(this IEndpointRouteBuilder app)
        {
            var staff = app.MapGroup("/api/prospectus")
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.Registrar), nameof(UserRole.AcademicHead),
                    nameof(UserRole.AdmissionOfficer), nameof(UserRole.SchoolAdmin)));

            staff.MapGet("programs", ProgramsAsync);
            staff.MapGet("curriculum.pdf", CurriculumPdfAsync);
            staff.MapGet("students/{registrationId:guid}/evaluation.pdf", EvaluationPdfAsync);
            staff.MapGet("students/{registrationId:guid}/registration-form.pdf", StaffRegistrationFormAsync);

            // A student's own copies. Same documents, resolved from their own account.
            var mine = app.MapGroup("/api/prospectus/me")
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.Student)));
            mine.MapGet("curriculum.pdf", MyCurriculumPdfAsync);
            mine.MapGet("registration-form.pdf", MyRegistrationFormAsync);
            return app;
        }

        /// <summary>The programs and year levels a prospectus can be printed for.</summary>
        private static async Task<IResult> ProgramsAsync(AppDbContext db, CancellationToken ct)
        {
            var curricula = await db.Curricula.AsNoTracking()
                .Where(c => !c.IsArchived)
                .OrderByDescending(c => c.IsActive).ThenBy(c => c.ProgramCode)
                .ToListAsync(ct);

            var ids = curricula.Select(c => c.Id).ToList();
            var counts = await db.Subjects.AsNoTracking()
                .Where(s => s.CurriculumId != null && ids.Contains(s.CurriculumId.Value) && !s.IsArchived)
                .GroupBy(s => new { s.CurriculumId, s.YearLevel })
                .Select(g => new { g.Key.CurriculumId, g.Key.YearLevel, Subjects = g.Count(), Units = g.Sum(s => s.Units) })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                programs = curricula.Select(c => new
                {
                    curriculumId = c.Id,
                    programCode = c.ProgramCode,
                    programName = c.ProgramName,
                    isActive = c.IsActive,
                    years = counts
                        .Where(x => x.CurriculumId == c.Id)
                        .OrderBy(x => x.YearLevel)
                        .Select(x => new
                        {
                            yearLevel = x.YearLevel,
                            label = YearLevelPolicy.Label(x.YearLevel),
                            subjectCount = x.Subjects,
                            units = x.Units
                        })
                        .ToList()
                }).ToList()
            });
        }

        // GET /api/prospectus/curriculum.pdf?curriculumId=&yearLevel=
        // Omit yearLevel to print the whole ladder.
        private static async Task<IResult> CurriculumPdfAsync(
            Guid? curriculumId, int? yearLevel, AppDbContext db, CancellationToken ct)
        {
            var curriculum = curriculumId is { } id
                ? await db.Curricula.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
                : await db.Curricula.AsNoTracking()
                    .Where(c => !c.IsArchived)
                    .OrderByDescending(c => c.IsActive)
                    .FirstOrDefaultAsync(ct);
            if (curriculum is null)
            {
                return Results.BadRequest(new { message = "No curriculum found. Set one up under Subjects & curriculum." });
            }
            if (yearLevel is { } y && !YearLevelPolicy.IsValid(y))
            {
                return Results.BadRequest(new
                {
                    message = $"Year level must be between {YearLevelPolicy.MinYearLevel} and {YearLevelPolicy.MaxYearLevel}."
                });
            }

            var bytes = await BuildCurriculumPdfAsync(db, curriculum, yearLevel, student: null, credited: null, ct);
            var suffix = yearLevel is { } yr ? $"-year{yr}" : string.Empty;
            return Results.File(bytes, Pdf, $"sengen-prospectus-{Slug(curriculum.ProgramCode)}{suffix}.pdf");
        }

        // GET /api/prospectus/me/curriculum.pdf — the signed-in student's own year-level sheet.
        // For an evaluated transferee this comes back with their credits marked.
        private static async Task<IResult> MyCurriculumPdfAsync(
            ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
        {
            var registration = await MyRegistrationAsync(principal, db, ct);
            if (registration is null) return NotLinked();

            var evaluation = await TransfereeEvaluationEndpoints
                .LoadEvaluationAsync(db, registration.Id, tracking: false, ct);
            var curriculum = await TransfereeEvaluationEndpoints
                .ResolveCurriculumAsync(db, registration, evaluation, ct);
            if (curriculum is null)
            {
                return Results.BadRequest(new { message = "No curriculum is set up for your program yet." });
            }

            var credited = CreditedCodes(evaluation);
            var bytes = await BuildCurriculumPdfAsync(
                db, curriculum, registration.YearLevel, HeaderFor(registration), credited, ct);
            return Results.File(bytes, Pdf, $"sengen-my-subjects-{Slug(registration.StudentNumber)}.pdf");
        }

        // GET /api/prospectus/students/{id}/evaluation.pdf — the signable credit ruling.
        private static async Task<IResult> EvaluationPdfAsync(
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

            var evaluation = await TransfereeEvaluationEndpoints
                .LoadEvaluationAsync(db, registrationId, tracking: false, ct);
            if (evaluation is null)
            {
                return Results.BadRequest(new
                {
                    message = "This transferee has not been evaluated yet — there is nothing to print."
                });
            }

            var curriculum = await TransfereeEvaluationEndpoints
                .ResolveCurriculumAsync(db, registration, evaluation, ct);
            var subjects = await TransfereeEvaluationEndpoints.CurriculumSubjectsAsync(db, curriculum, ct);
            var byId = evaluation.Items.ToDictionary(i => i.SubjectId);

            var rows = subjects
                .Where(s => byId.ContainsKey(s.Id) && byId[s.Id].Decision != SubjectCreditDecision.Undecided)
                .Select(s =>
                {
                    var item = byId[s.Id];
                    return new EvaluationPdfRow(
                        s.YearLevel,
                        EvaluationMapping.TermLabel(s.Term),
                        s.Code, s.Title, s.Units,
                        item.Decision == SubjectCreditDecision.Credited,
                        item.SourceSubject, item.SourceGrade);
                })
                .OrderBy(r => r.YearLevel).ThenBy(r => r.TermLabel).ThenBy(r => r.Code)
                .ToList();

            var evaluator = evaluation.EvaluatedByUserId is { } userId
                ? await db.Users.AsNoTracking().Where(u => u.Id == userId)
                    .Select(u => u.FullName).FirstOrDefaultAsync(ct)
                : null;

            var totals = TransfereeEvaluationEndpoints.Totals(evaluation);
            var document = new EvaluationPdfDocument(
                registration.FullName,
                registration.OfficialStudentNumber ?? registration.StudentNumber,
                registration.Program.ToString(),
                registration.SchoolName,
                curriculum is null ? null : $"{curriculum.ProgramCode} — {curriculum.ProgramName}",
                StatusLabel(evaluation.Status),
                totals.Credited,
                totals.ToTake,
                evaluation.AssignedYearLevel,
                evaluation.RecommendedYearLevel,
                evaluation.Remarks,
                evaluator,
                evaluation.EvaluatedAtUtc,
                rows,
                DateTime.Now);

            return Results.File(document.GeneratePdf(), Pdf,
                $"sengen-evaluation-{Slug(registration.StudentNumber)}.pdf");
        }

        private static Task<IResult> StaffRegistrationFormAsync(
            Guid registrationId, AppDbContext db, CancellationToken ct) =>
            RegistrationFormAsync(registrationId, db, ct);

        private static async Task<IResult> MyRegistrationFormAsync(
            ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
        {
            var registration = await MyRegistrationAsync(principal, db, ct);
            return registration is null ? NotLinked() : await RegistrationFormAsync(registration.Id, db, ct);
        }

        /// <summary>
        /// The Certificate of Registration: approved seats joined to the published timetable. A
        /// subject with an approved seat but no published class still prints — the student is
        /// enrolled in it either way, and hiding it would make the form quietly wrong.
        /// </summary>
        private static async Task<IResult> RegistrationFormAsync(
            Guid registrationId, AppDbContext db, CancellationToken ct)
        {
            var registration = await db.StudentRegistrations.AsNoTracking()
                .Include(r => r.Semester)
                .FirstOrDefaultAsync(r => r.Id == registrationId, ct);
            if (registration is null)
            {
                return Results.NotFound(new { message = "Registration not found." });
            }

            var approvedSectionIds = await db.SlotRequests.AsNoTracking()
                .Where(r => r.StudentRegistrationId == registrationId && r.Status == SlotRequestStatus.Approved)
                .Select(r => r.SectionId)
                .ToListAsync(ct);

            var sections = await db.Sections.AsNoTracking()
                .Where(s => approvedSectionIds.Contains(s.Id))
                .Include(s => s.Subject)
                .ToListAsync(ct);

            var assignments = await db.ScheduleAssignments.AsNoTracking()
                .Where(a => approvedSectionIds.Contains(a.SectionId) && a.IsPublished)
                .Include(a => a.Room)
                .Include(a => a.TimeSlot)
                .Include(a => a.FacultyProfile).ThenInclude(f => f!.User)
                .ToListAsync(ct);

            var rows = sections
                .OrderBy(s => s.Subject?.Code)
                .Select(section =>
                {
                    var meetings = assignments
                        .Where(a => a.SectionId == section.Id && a.TimeSlot is not null)
                        .OrderBy(a => a.TimeSlot!.Day).ThenBy(a => a.TimeSlot!.StartMinutes)
                        .ToList();
                    return new EnrolledClassRow(
                        section.Subject?.Code ?? string.Empty,
                        section.Subject?.Title ?? string.Empty,
                        section.Subject?.Units ?? 0,
                        section.SectionCode,
                        meetings.Count == 0 ? "—" : string.Join(", ", meetings
                            .Select(a => DayAbbr(a.TimeSlot!.Day)).Distinct()),
                        meetings.Count == 0 ? "Not yet scheduled" : string.Join(", ", meetings
                            .Select(a => $"{Hhmm(a.TimeSlot!.StartMinutes)}–{Hhmm(a.TimeSlot.EndMinutes)}")
                            .Distinct()),
                        meetings.Count == 0 ? "—" : string.Join(", ", meetings
                            .Select(a => a.Room?.Name ?? "—").Distinct()),
                        meetings.FirstOrDefault()?.FacultyProfile?.User?.FullName ?? "—",
                        meetings.Any(a => a.IsAmended));
                })
                .ToList();

            var document = new RegistrationFormPdfDocument(
                registration.FullName,
                registration.OfficialStudentNumber ?? registration.StudentNumber,
                registration.Program.ToString(),
                YearLevelPolicy.Label(registration.YearLevel),
                registration.StudentType == StudentType.Transferee ? "Transferee" : "New student",
                registration.Semester?.Name ?? "the active term",
                rows,
                DateTime.Now);

            return Results.File(document.GeneratePdf(), Pdf,
                $"sengen-registration-{Slug(registration.StudentNumber)}.pdf");
        }

        // ---- shared building blocks ----

        private static async Task<byte[]> BuildCurriculumPdfAsync(
            AppDbContext db,
            Domain.Curriculum curriculum,
            int? yearLevel,
            StudentProspectusHeader? student,
            IReadOnlySet<string>? credited,
            CancellationToken ct)
        {
            var subjects = await db.Subjects.AsNoTracking()
                .Where(s => s.CurriculumId == curriculum.Id && !s.IsArchived)
                .Where(s => yearLevel == null || s.YearLevel == yearLevel)
                .Include(s => s.Prerequisites).ThenInclude(p => p.PrerequisiteSubject)
                .OrderBy(s => s.YearLevel).ThenBy(s => s.Term).ThenBy(s => s.Code)
                .ToListAsync(ct);

            var years = subjects
                .GroupBy(s => s.YearLevel)
                .OrderBy(g => g.Key)
                .Select(g => new ProspectusYear(
                    g.Key,
                    g.Where(s => s.Term != SemesterTerm.SecondSemester).Select(Row).ToList(),
                    g.Where(s => s.Term == SemesterTerm.SecondSemester).Select(Row).ToList()))
                .ToList();

            var note = yearLevel is { } y
                ? $"{YearLevelPolicy.Label(y)} · {(curriculum.IsActive ? "active curriculum" : "curriculum")}"
                : curriculum.IsActive ? "Active curriculum · all year levels" : "All year levels";

            var document = new ProspectusPdfDocument(
                curriculum.ProgramCode,
                curriculum.ProgramName,
                note,
                years,
                student,
                credited ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                DateTime.Now);
            return document.GeneratePdf();

            static ProspectusRow Row(Subject s) => new(
                s.Code, s.Title, s.Units, s.LectureHours, s.LaboratoryHours,
                string.Join(", ", s.Prerequisites
                    .Select(p => p.PrerequisiteSubject?.Code)
                    .Where(c => !string.IsNullOrWhiteSpace(c))!));
        }

        private static async Task<StudentRegistration?> MyRegistrationAsync(
            ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
        {
            if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return null;
            }
            return await db.StudentRegistrations.AsNoTracking()
                .Include(r => r.Semester)
                .FirstOrDefaultAsync(r => r.UserId == userId, ct);
        }

        private static IResult NotLinked() => Results.BadRequest(new
        {
            message = "Your account is not linked to a student record yet. Claim it under Document requirements."
        });

        private static StudentProspectusHeader HeaderFor(StudentRegistration r) => new(
            r.FullName,
            r.OfficialStudentNumber ?? r.StudentNumber,
            r.StudentType == StudentType.Transferee ? "Transferee" : "New student",
            YearLevelPolicy.Label(r.YearLevel));

        private static HashSet<string> CreditedCodes(Domain.TransfereeEvaluation? evaluation) =>
            evaluation is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : evaluation.Items
                    .Where(i => i.Decision == SubjectCreditDecision.Credited && i.Subject is not null)
                    .Select(i => i.Subject!.Code)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

        private static string StatusLabel(TransfereeEvaluationStatus status) => status switch
        {
            TransfereeEvaluationStatus.Completed => "Completed",
            TransfereeEvaluationStatus.InProgress => "In progress — not yet signed off",
            _ => "Pending"
        };

        private static string DayAbbr(DayOfWeek day) => day switch
        {
            DayOfWeek.Monday => "M",
            DayOfWeek.Tuesday => "T",
            DayOfWeek.Wednesday => "W",
            DayOfWeek.Thursday => "Th",
            DayOfWeek.Friday => "F",
            DayOfWeek.Saturday => "S",
            _ => day.ToString()
        };

        private static string Hhmm(int minutes) => $"{minutes / 60:D2}:{minutes % 60:D2}";

        private static string Slug(string value)
        {
            var chars = value.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray();
            return chars.Length == 0 ? "student" : new string(chars).ToLowerInvariant();
        }
    }
}
