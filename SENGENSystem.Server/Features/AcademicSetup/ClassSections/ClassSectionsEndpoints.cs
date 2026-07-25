using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.AcademicSetup.ClassSections
{
    // Vertical slice: the Academic Head (and School Admin) define student class blocks for a semester
    // (term) — a course (program) at a year level, split into a named section (e.g. BSCS · Year 3 · "A").
    // Classes are created afresh each semester; the cohort is the block a curriculum's subjects deliver to.
    public record ClassSectionRequest(Guid? SemesterId, string? ProgramCode, int? YearLevel, string? SectionName, Guid? CurriculumId);

    // Lightweight option for the class-section modal's curriculum picker. Archived catalogs are
    // offered too — a returning cohort deliberately stays on the retired curriculum (FR-SCHED-04).
    public record CurriculumOptionDto(Guid Id, string ProgramCode, string Label, bool IsActive, bool IsArchived);

    public static class ClassSectionsEndpoints
    {
        public static IEndpointRouteBuilder MapClassSections(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/class-sections")
                .RequireAuthorization(policy =>
                    policy.RequireRole(nameof(UserRole.AcademicHead), nameof(UserRole.SchoolAdmin)));

            group.MapGet("", ListAsync);
            group.MapPost("", CreateAsync);
            group.MapPut("/{id:guid}", UpdateAsync);
            group.MapDelete("/{id:guid}", DeleteAsync);
            return app;
        }

        // GET /api/class-sections?semesterId=&programCode= — classes for the semester, plus the list of
        // semesters to choose from (the Academic Head can't read the SchoolAdmin semesters endpoint, so
        // the selector is sourced here) and the programs to choose a course from.
        private static async Task<IResult> ListAsync(
            Guid? semesterId, string? programCode, AppDbContext db, CancellationToken ct)
        {
            var semesters = await db.Semesters.AsNoTracking()
                .OrderByDescending(s => s.StartDate)
                .Select(s => new { id = s.Id, name = s.Name, isActive = s.IsActive })
                .ToListAsync(ct);

            var programs = await db.Curricula.AsNoTracking()
                .GroupBy(c => c.ProgramCode)
                .Select(g => new { code = g.Key, name = g.Max(c => c.ProgramName) })
                .OrderBy(p => p.code)
                .ToListAsync(ct);

            // Every curriculum (active and archived) so the modal can attach a cohort to the exact
            // catalog it follows — the whole point of per-section curricula (FR-SCHED-04).
            var curriculumEntities = await db.Curricula.AsNoTracking()
                .Include(c => c.SchoolYears).ThenInclude(l => l.SchoolYear)
                .OrderBy(c => c.ProgramCode).ThenBy(c => c.IsArchived)
                .ToListAsync(ct);
            var curricula = curriculumEntities
                .Select(c => new CurriculumOptionDto(c.Id, c.ProgramCode, CurriculumLabel(c), c.IsActive, c.IsArchived))
                .ToList();

            var semester = await ResolveSemesterAsync(semesterId, db, ct);
            if (semester is null)
            {
                return Results.Ok(new
                {
                    semesterId = (Guid?)null,
                    semesterName = (string?)null,
                    semesters,
                    programs,
                    curricula,
                    count = 0,
                    classSections = Array.Empty<ClassSectionDto>()
                });
            }

            var query = db.ClassSections.AsNoTracking()
                .Include(c => c.Semester)
                .Include(c => c.Curriculum).ThenInclude(cu => cu!.SchoolYears).ThenInclude(l => l.SchoolYear)
                .Where(c => c.SemesterId == semester.Id);
            if (!string.IsNullOrWhiteSpace(programCode))
            {
                query = query.Where(c => c.ProgramCode == programCode);
            }

            var sections = await query
                .OrderBy(c => c.ProgramCode).ThenBy(c => c.YearLevel).ThenBy(c => c.SectionName)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                semesterId = semester.Id,
                semesterName = semester.Name,
                semesters,
                programs,
                curricula,
                count = sections.Count,
                classSections = sections.Select(ClassSectionDto.From).ToList()
            });
        }

        /// <summary>
        /// Identifies a curriculum for the picker/chip by its program and effectivity years, so old
        /// and new versions of the same program read apart (e.g. "ITP · AY 2025-2026 (archived)").
        /// </summary>
        internal static string CurriculumLabel(Domain.Curriculum c)
        {
            var years = c.SchoolYears
                .Select(l => l.SchoolYear?.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(n => n)
                .ToList();
            var basis = years.Count > 0 ? string.Join(", ", years) : c.ProgramName;
            var tag = c.IsArchived ? " (archived)" : c.IsActive ? " (active)" : string.Empty;
            return $"{c.ProgramCode} · {basis}{tag}";
        }

        private static async Task<IResult> CreateAsync(
            ClassSectionRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var (ok, semester, programCode, yearLevel, sectionName, curriculumId, problem) = await ValidateAsync(request, db, null, ct);
            if (!ok) return problem;

            var section = new ClassSection
            {
                SemesterId = semester!.Id,
                ProgramCode = programCode,
                YearLevel = yearLevel,
                SectionName = sectionName,
                CurriculumId = curriculumId
            };
            db.ClassSections.Add(section);
            audit.Record(AuditAction.ClassSectionSaved,
                $"Created class “{section.DisplayName}” for {semester.Name}.",
                "ClassSection", section.Id.ToString());
            await db.SaveChangesAsync(ct);

            section.Semester = semester;
            section.Curriculum = await LoadCurriculumAsync(db, curriculumId, ct);
            return Results.Created($"/api/class-sections/{section.Id}", ClassSectionDto.From(section));
        }

        private static async Task<IResult> UpdateAsync(
            Guid id, ClassSectionRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var section = await db.ClassSections.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (section is null) return Results.NotFound(new { message = "Class section not found." });

            var (ok, semester, programCode, yearLevel, sectionName, curriculumId, problem) = await ValidateAsync(request, db, id, ct);
            if (!ok) return problem;

            section.SemesterId = semester!.Id;
            section.ProgramCode = programCode;
            section.YearLevel = yearLevel;
            section.SectionName = sectionName;
            section.CurriculumId = curriculumId;

            audit.Record(AuditAction.ClassSectionSaved,
                $"Updated class “{section.DisplayName}” for {semester.Name}.",
                "ClassSection", section.Id.ToString());
            await db.SaveChangesAsync(ct);

            section.Semester = semester;
            section.Curriculum = await LoadCurriculumAsync(db, curriculumId, ct);
            return Results.Ok(ClassSectionDto.From(section));
        }

        private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var section = await db.ClassSections.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (section is null) return Results.NotFound(new { message = "Class section not found." });

            if (await db.FacultyLoadAssignments.AnyAsync(l => l.ClassSectionId == id, ct))
            {
                return Results.Conflict(new { message = "This class has faculty load assigned and can't be deleted. Remove the load first." });
            }

            db.ClassSections.Remove(section);
            audit.Record(AuditAction.ClassSectionSaved, $"Deleted class “{section.DisplayName}”.",
                "ClassSection", section.Id.ToString());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }

        private static async Task<(bool Ok, Semester? Semester, string ProgramCode, int YearLevel, string SectionName, Guid? CurriculumId, IResult Problem)>
            ValidateAsync(ClassSectionRequest request, AppDbContext db, Guid? id, CancellationToken ct)
        {
            var programCode = request.ProgramCode?.Trim() ?? string.Empty;
            var sectionName = request.SectionName?.Trim() ?? string.Empty;
            var yearLevel = request.YearLevel ?? 0;
            var errors = new Dictionary<string, string[]>();

            var semester = request.SemesterId is { } sid
                ? await db.Semesters.FirstOrDefaultAsync(s => s.Id == sid, ct)
                : null;
            if (request.SemesterId is null) errors["semesterId"] = ["Please choose a semester."];
            else if (semester is null) errors["semesterId"] = ["The selected semester no longer exists."];

            if (string.IsNullOrWhiteSpace(programCode)) errors["programCode"] = ["Please choose a course."];
            // 1–3, matching Subject.YearLevel. Pinning this to 3 made the two-year courses
            // (HRS and ITP) impossible to run: every cohort in them is Year 1 or Year 2.
            if (yearLevel is < 1 or > 3) errors["yearLevel"] = ["Year level must be between 1 and 3."];
            if (string.IsNullOrWhiteSpace(sectionName)) errors["sectionName"] = ["A section name is required."];

            // Resolve the cohort's curriculum: an explicit choice must exist and belong to the same
            // program; when omitted, default to the program's active curriculum (FR-SCHED-04).
            Guid? curriculumId = null;
            if (!string.IsNullOrWhiteSpace(programCode))
            {
                if (request.CurriculumId is { } cid)
                {
                    var curriculum = await db.Curricula.FirstOrDefaultAsync(c => c.Id == cid, ct);
                    if (curriculum is null)
                    {
                        errors["curriculumId"] = ["The selected curriculum no longer exists."];
                    }
                    else if (!string.Equals(curriculum.ProgramCode, programCode, StringComparison.OrdinalIgnoreCase))
                    {
                        errors["curriculumId"] = [$"That curriculum belongs to {curriculum.ProgramCode}, not {programCode}."];
                    }
                    else
                    {
                        curriculumId = curriculum.Id;
                    }
                }
                else
                {
                    curriculumId = await db.Curricula
                        .Where(c => c.ProgramCode == programCode && c.IsActive && !c.IsArchived)
                        .Select(c => (Guid?)c.Id)
                        .FirstOrDefaultAsync(ct);
                }
            }

            if (errors.Count == 0)
            {
                var duplicate = await db.ClassSections.AnyAsync(c =>
                    c.Id != id && c.SemesterId == semester!.Id && c.ProgramCode == programCode
                    && c.YearLevel == yearLevel && c.SectionName == sectionName, ct);
                if (duplicate)
                {
                    errors["sectionName"] = [$"{programCode} Year {yearLevel} section “{sectionName}” already exists for {semester!.Name}."];
                }
            }

            if (errors.Count > 0)
            {
                return (false, semester, programCode, yearLevel, sectionName, curriculumId, Results.ValidationProblem(errors));
            }

            return (true, semester, programCode, yearLevel, sectionName, curriculumId, Results.Empty);
        }

        // Reloads the chosen curriculum with its school years so the returned row carries a label.
        private static async Task<Domain.Curriculum?> LoadCurriculumAsync(AppDbContext db, Guid? curriculumId, CancellationToken ct) =>
            curriculumId is { } id
                ? await db.Curricula.AsNoTracking()
                    .Include(c => c.SchoolYears).ThenInclude(l => l.SchoolYear)
                    .FirstOrDefaultAsync(c => c.Id == id, ct)
                : null;

        private static async Task<Semester?> ResolveSemesterAsync(Guid? semesterId, AppDbContext db, CancellationToken ct) =>
            semesterId is { } id
                ? await db.Semesters.FirstOrDefaultAsync(s => s.Id == id, ct)
                : await db.Semesters.FirstOrDefaultAsync(s => s.IsActive, ct)
                    ?? await db.Semesters.OrderByDescending(s => s.StartDate).FirstOrDefaultAsync(ct);
    }
}
