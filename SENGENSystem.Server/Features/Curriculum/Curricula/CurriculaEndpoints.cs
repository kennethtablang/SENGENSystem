using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using CurriculumEntity = SENGENSystem.Server.Domain.Curriculum;

namespace SENGENSystem.Server.Features.Curriculum.Curricula
{
    // Vertical slice: the Academic Head manages program curricula — versioned catalogs of subjects
    // (FR-SCHED-04). Activating a curriculum makes it the current catalog for its program (other
    // versions of the same program are deactivated). A curriculum with subjects can't be deleted.
    public record CurriculumRequest(string? ProgramCode, string? ProgramName, int? EffectivityYear);

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
            var items = await db.Curricula
                .AsNoTracking()
                .OrderBy(c => c.ProgramCode)
                .ThenByDescending(c => c.EffectivityYear)
                .Select(c => new { Curriculum = c, Count = db.Subjects.Count(s => s.CurriculumId == c.Id) })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                count = items.Count,
                curricula = items.Select(x => CurriculumDto.From(x.Curriculum, x.Count)).ToList()
            });
        }

        private static async Task<IResult> CreateAsync(
            CurriculumRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            if (!Validate(request, out var code, out var name, out var year, out var problem)) return problem;

            if (await db.Curricula.AnyAsync(c => c.ProgramCode == code && c.EffectivityYear == year, ct))
            {
                return Results.Conflict(new { message = $"A {code} curriculum for {year} already exists." });
            }

            var curriculum = new CurriculumEntity { ProgramCode = code, ProgramName = name, EffectivityYear = year };
            db.Curricula.Add(curriculum);
            audit.Record(AuditAction.CurriculumSaved, $"Created curriculum {curriculum.ProgramCode} {curriculum.EffectivityYear}.",
                "Curriculum", curriculum.Id.ToString());
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/curricula/{curriculum.Id}", CurriculumDto.From(curriculum, 0));
        }

        private static async Task<IResult> UpdateAsync(
            Guid id, CurriculumRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var curriculum = await db.Curricula.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (curriculum is null) return Results.NotFound(new { message = "Curriculum not found." });

            if (!Validate(request, out var code, out var name, out var year, out var problem)) return problem;

            if (await db.Curricula.AnyAsync(c => c.ProgramCode == code && c.EffectivityYear == year && c.Id != id, ct))
            {
                return Results.Conflict(new { message = $"A {code} curriculum for {year} already exists." });
            }

            // Keep member subjects' denormalized ProgramCode aligned with the curriculum's program.
            if (curriculum.ProgramCode != code)
            {
                await db.Subjects.Where(s => s.CurriculumId == id).ExecuteUpdateAsync(
                    su => su.SetProperty(s => s.ProgramCode, code), ct);
            }

            curriculum.ProgramCode = code;
            curriculum.ProgramName = name;
            curriculum.EffectivityYear = year;

            audit.Record(AuditAction.CurriculumSaved, $"Updated curriculum {curriculum.ProgramCode} {curriculum.EffectivityYear}.",
                "Curriculum", curriculum.Id.ToString());
            await db.SaveChangesAsync(ct);

            var count = await db.Subjects.CountAsync(s => s.CurriculumId == id, ct);
            return Results.Ok(CurriculumDto.From(curriculum, count));
        }

        private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var curriculum = await db.Curricula.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (curriculum is null) return Results.NotFound(new { message = "Curriculum not found." });

            if (await db.Subjects.AnyAsync(s => s.CurriculumId == id, ct))
            {
                return Results.Conflict(new { message = "This curriculum still has subjects. Delete or move them first." });
            }

            db.Curricula.Remove(curriculum);
            audit.Record(AuditAction.CurriculumSaved, $"Deleted curriculum {curriculum.ProgramCode} {curriculum.EffectivityYear}.",
                "Curriculum", curriculum.Id.ToString());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }

        private static async Task<IResult> SetActiveAsync(Guid id, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var curriculum = await db.Curricula.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (curriculum is null) return Results.NotFound(new { message = "Curriculum not found." });

            // Single active catalog per program: clear the flag on other versions of this program.
            await db.Curricula
                .Where(c => c.ProgramCode == curriculum.ProgramCode && c.IsActive && c.Id != id)
                .ExecuteUpdateAsync(su => su.SetProperty(c => c.IsActive, false), ct);

            if (!curriculum.IsActive)
            {
                curriculum.IsActive = true;
                audit.Record(AuditAction.CurriculumSaved,
                    $"Set {curriculum.ProgramCode} {curriculum.EffectivityYear} as the active curriculum.",
                    "Curriculum", curriculum.Id.ToString());
            }
            await db.SaveChangesAsync(ct);

            var count = await db.Subjects.CountAsync(s => s.CurriculumId == id, ct);
            return Results.Ok(CurriculumDto.From(curriculum, count));
        }

        private static bool Validate(
            CurriculumRequest request, out string code, out string name, out int year, out IResult problem)
        {
            code = request.ProgramCode?.Trim().ToUpperInvariant() ?? string.Empty;
            name = request.ProgramName?.Trim() ?? string.Empty;
            year = request.EffectivityYear ?? 0;
            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(code)) errors["programCode"] = ["A program code is required."];
            if (string.IsNullOrWhiteSpace(name)) errors["programName"] = ["A program name is required."];
            if (year is < 2000 or > 2100) errors["effectivityYear"] = ["Enter an effectivity year between 2000 and 2100."];

            if (errors.Count > 0)
            {
                problem = Results.ValidationProblem(errors);
                return false;
            }

            problem = Results.Empty;
            return true;
        }
    }
}
