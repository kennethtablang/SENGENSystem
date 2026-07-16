using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.AcademicSetup.ClassSections
{
    // Vertical slice: the Academic Head (and School Admin) define student class blocks for a semester
    // (term) — a course (program) at a year level, split into a named section (e.g. BSCS · Year 3 · "A").
    // Classes are created afresh each semester; the cohort is the block a curriculum's subjects deliver to.
    public record ClassSectionRequest(Guid? SemesterId, string? ProgramCode, int? YearLevel, string? SectionName);

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

            var semester = await ResolveSemesterAsync(semesterId, db, ct);
            if (semester is null)
            {
                return Results.Ok(new
                {
                    semesterId = (Guid?)null,
                    semesterName = (string?)null,
                    semesters,
                    programs,
                    count = 0,
                    classSections = Array.Empty<ClassSectionDto>()
                });
            }

            var query = db.ClassSections.AsNoTracking()
                .Include(c => c.Semester)
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
                count = sections.Count,
                classSections = sections.Select(ClassSectionDto.From).ToList()
            });
        }

        private static async Task<IResult> CreateAsync(
            ClassSectionRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var (ok, semester, programCode, yearLevel, sectionName, problem) = await ValidateAsync(request, db, null, ct);
            if (!ok) return problem;

            var section = new ClassSection
            {
                SemesterId = semester!.Id,
                ProgramCode = programCode,
                YearLevel = yearLevel,
                SectionName = sectionName
            };
            db.ClassSections.Add(section);
            audit.Record(AuditAction.ClassSectionSaved,
                $"Created class “{section.DisplayName}” for {semester.Name}.",
                "ClassSection", section.Id.ToString());
            await db.SaveChangesAsync(ct);

            section.Semester = semester;
            return Results.Created($"/api/class-sections/{section.Id}", ClassSectionDto.From(section));
        }

        private static async Task<IResult> UpdateAsync(
            Guid id, ClassSectionRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var section = await db.ClassSections.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (section is null) return Results.NotFound(new { message = "Class section not found." });

            var (ok, semester, programCode, yearLevel, sectionName, problem) = await ValidateAsync(request, db, id, ct);
            if (!ok) return problem;

            section.SemesterId = semester!.Id;
            section.ProgramCode = programCode;
            section.YearLevel = yearLevel;
            section.SectionName = sectionName;

            audit.Record(AuditAction.ClassSectionSaved,
                $"Updated class “{section.DisplayName}” for {semester.Name}.",
                "ClassSection", section.Id.ToString());
            await db.SaveChangesAsync(ct);

            section.Semester = semester;
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

        private static async Task<(bool Ok, Semester? Semester, string ProgramCode, int YearLevel, string SectionName, IResult Problem)>
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
            if (yearLevel != 3) errors["yearLevel"] = ["Year level must be 3."];
            if (string.IsNullOrWhiteSpace(sectionName)) errors["sectionName"] = ["A section name is required."];

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
                return (false, semester, programCode, yearLevel, sectionName, Results.ValidationProblem(errors));
            }

            return (true, semester, programCode, yearLevel, sectionName, Results.Empty);
        }

        private static async Task<Semester?> ResolveSemesterAsync(Guid? semesterId, AppDbContext db, CancellationToken ct) =>
            semesterId is { } id
                ? await db.Semesters.FirstOrDefaultAsync(s => s.Id == id, ct)
                : await db.Semesters.FirstOrDefaultAsync(s => s.IsActive, ct)
                    ?? await db.Semesters.OrderByDescending(s => s.StartDate).FirstOrDefaultAsync(ct);
    }
}
