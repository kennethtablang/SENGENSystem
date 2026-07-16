using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using CurriculumEntity = SENGENSystem.Server.Domain.Curriculum;

namespace SENGENSystem.Server.Features.Curriculum.Curricula
{
    // Vertical slice: the Academic Head manages program curricula (FR-SCHED-04). A curriculum is
    // effective for one or more school years (checkbox effectivity); a program's school year maps
    // to at most one curriculum. Activating one makes it the current catalog for its program.
    public record CurriculumRequest(string? ProgramCode, string? ProgramName, List<Guid>? SchoolYearIds);

    public static class CurriculaEndpoints
    {
        public static IEndpointRouteBuilder MapCurricula(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/curricula")
                .RequireAuthorization(policy =>
                    policy.RequireRole(nameof(UserRole.AcademicHead), nameof(UserRole.SchoolAdmin)));

            group.MapGet("", ListAsync);
            group.MapPost("", CreateAsync);
            group.MapPut("/{id:guid}", UpdateAsync);
            group.MapDelete("/{id:guid}", DeleteAsync);
            group.MapPost("/{id:guid}/active", SetActiveAsync);
            return app;
        }

        private static async Task<IResult> ListAsync(AppDbContext db, CancellationToken ct)
        {
            var curricula = await db.Curricula.AsNoTracking()
                .Include(c => c.SchoolYears).ThenInclude(l => l.SchoolYear)
                .OrderBy(c => c.ProgramCode)
                .ToListAsync(ct);

            var counts = await db.Subjects
                .Where(s => s.CurriculumId != null)
                .GroupBy(s => s.CurriculumId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

            // All school years for the modal's effectivity checkbox list (Academic Head can't read
            // the SchoolAdmin-only /api/school-years).
            var schoolYears = await db.SchoolYears.AsNoTracking()
                .OrderByDescending(y => y.StartDate)
                .Select(y => new { y.Id, y.Name, y.IsActive })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                count = curricula.Count,
                curricula = curricula.Select(c => CurriculumDto.From(c, counts.GetValueOrDefault(c.Id))).ToList(),
                schoolYears
            });
        }

        private static async Task<IResult> CreateAsync(
            CurriculumRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var (ok, code, name, yearIds, problem) = await ValidateAsync(request, null, db, ct);
            if (!ok) return problem;

            var curriculum = new CurriculumEntity { ProgramCode = code, ProgramName = name };
            db.Curricula.Add(curriculum);
            foreach (var yearId in yearIds)
            {
                db.CurriculumSchoolYears.Add(new CurriculumSchoolYear { CurriculumId = curriculum.Id, SchoolYearId = yearId });
            }

            audit.Record(AuditAction.CurriculumSaved, $"Created the {curriculum.ProgramCode} curriculum.",
                "Curriculum", curriculum.Id.ToString());
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/curricula/{curriculum.Id}", await LoadDtoAsync(db, curriculum.Id, ct));
        }

        private static async Task<IResult> UpdateAsync(
            Guid id, CurriculumRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var curriculum = await db.Curricula.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (curriculum is null) return Results.NotFound(new { message = "Curriculum not found." });

            var (ok, code, name, yearIds, problem) = await ValidateAsync(request, id, db, ct);
            if (!ok) return problem;

            // Keep member subjects' denormalized ProgramCode aligned with the curriculum's program.
            if (curriculum.ProgramCode != code)
            {
                await db.Subjects.Where(s => s.CurriculumId == id).ExecuteUpdateAsync(
                    su => su.SetProperty(s => s.ProgramCode, code), ct);
            }

            curriculum.ProgramCode = code;
            curriculum.ProgramName = name;
            await ReconcileSchoolYearsAsync(db, id, yearIds, ct);

            audit.Record(AuditAction.CurriculumSaved, $"Updated the {curriculum.ProgramCode} curriculum.",
                "Curriculum", curriculum.Id.ToString());
            await db.SaveChangesAsync(ct);

            return Results.Ok(await LoadDtoAsync(db, id, ct));
        }

        private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var curriculum = await db.Curricula.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (curriculum is null) return Results.NotFound(new { message = "Curriculum not found." });

            if (await db.Subjects.AnyAsync(s => s.CurriculumId == id, ct))
            {
                return Results.Conflict(new { message = "This curriculum still has subjects. Delete or move them first." });
            }

            db.Curricula.Remove(curriculum); // effectivity links cascade away
            audit.Record(AuditAction.CurriculumSaved, $"Deleted the {curriculum.ProgramCode} curriculum.",
                "Curriculum", curriculum.Id.ToString());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }

        private static async Task<IResult> SetActiveAsync(Guid id, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var curriculum = await db.Curricula.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (curriculum is null) return Results.NotFound(new { message = "Curriculum not found." });

            // Single active catalog per program: clear the flag on other curricula of this program.
            await db.Curricula
                .Where(c => c.ProgramCode == curriculum.ProgramCode && c.IsActive && c.Id != id)
                .ExecuteUpdateAsync(su => su.SetProperty(c => c.IsActive, false), ct);

            if (!curriculum.IsActive)
            {
                curriculum.IsActive = true;
                audit.Record(AuditAction.CurriculumSaved,
                    $"Set the {curriculum.ProgramCode} curriculum as active.", "Curriculum", curriculum.Id.ToString());
            }
            await db.SaveChangesAsync(ct);

            return Results.Ok(await LoadDtoAsync(db, id, ct));
        }

        private static async Task ReconcileSchoolYearsAsync(AppDbContext db, Guid curriculumId, List<Guid> desired, CancellationToken ct)
        {
            var existing = await db.CurriculumSchoolYears.Where(l => l.CurriculumId == curriculumId).ToListAsync(ct);
            foreach (var gone in existing.Where(e => !desired.Contains(e.SchoolYearId)))
            {
                db.CurriculumSchoolYears.Remove(gone);
            }
            foreach (var addId in desired.Where(d => existing.All(e => e.SchoolYearId != d)))
            {
                db.CurriculumSchoolYears.Add(new CurriculumSchoolYear { CurriculumId = curriculumId, SchoolYearId = addId });
            }
        }

        private static async Task<CurriculumDto> LoadDtoAsync(AppDbContext db, Guid id, CancellationToken ct)
        {
            var curriculum = await db.Curricula.AsNoTracking()
                .Include(c => c.SchoolYears).ThenInclude(l => l.SchoolYear)
                .FirstAsync(c => c.Id == id, ct);
            var count = await db.Subjects.CountAsync(s => s.CurriculumId == id, ct);
            return CurriculumDto.From(curriculum, count);
        }

        private static async Task<(bool Ok, string Code, string Name, List<Guid> SchoolYearIds, IResult Problem)>
            ValidateAsync(CurriculumRequest request, Guid? excludeId, AppDbContext db, CancellationToken ct)
        {
            var code = request.ProgramCode?.Trim().ToUpperInvariant() ?? string.Empty;
            var name = request.ProgramName?.Trim() ?? string.Empty;
            var yearIds = request.SchoolYearIds?.Distinct().ToList() ?? new List<Guid>();
            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(code)) errors["programCode"] = ["A program code is required."];
            if (string.IsNullOrWhiteSpace(name)) errors["programName"] = ["A program name is required."];

            if (yearIds.Count == 0)
            {
                errors["schoolYearIds"] = ["Select at least one school year this curriculum is effective for."];
            }
            else
            {
                var existingYears = await db.SchoolYears.Where(y => yearIds.Contains(y.Id)).Select(y => y.Id).ToListAsync(ct);
                if (existingYears.Count != yearIds.Count)
                {
                    errors["schoolYearIds"] = ["One of the selected school years no longer exists."];
                }
                else if (!string.IsNullOrWhiteSpace(code))
                {
                    // A program's school year can belong to only one curriculum.
                    var clash = await db.CurriculumSchoolYears
                        .Include(l => l.Curriculum).Include(l => l.SchoolYear)
                        .Where(l => yearIds.Contains(l.SchoolYearId)
                            && l.Curriculum!.ProgramCode == code
                            && l.CurriculumId != excludeId)
                        .Select(l => l.SchoolYear!.Name)
                        .FirstOrDefaultAsync(ct);
                    if (clash is not null)
                    {
                        errors["schoolYearIds"] = [$"{clash} is already covered by another {code} curriculum."];
                    }
                }
            }

            if (errors.Count > 0)
            {
                return (false, code, name, yearIds, Results.ValidationProblem(errors));
            }

            return (true, code, name, yearIds, Results.Empty);
        }
    }
}
