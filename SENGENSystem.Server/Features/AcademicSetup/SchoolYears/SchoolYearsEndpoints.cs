using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.AcademicSetup.SchoolYears
{
    // Vertical slice: the School Admin manages the academic calendar's top level — school years
    // that group semesters. Full CRUD, single-active selection, and a safe delete that refuses
    // to remove a year that still has semesters filed under it (409).
    public record SchoolYearRequest(string? Name, string? StartDate, string? EndDate);

    public static class SchoolYearsEndpoints
    {
        public static IEndpointRouteBuilder MapSchoolYears(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/school-years")
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.SchoolAdmin)));

            group.MapGet("", ListAsync);
            group.MapPost("", CreateAsync);
            group.MapPut("/{id:guid}", UpdateAsync);
            group.MapDelete("/{id:guid}", DeleteAsync);
            group.MapPost("/{id:guid}/active", SetActiveAsync);
            return app;
        }

        private static async Task<IResult> ListAsync(AppDbContext db, CancellationToken ct)
        {
            var years = await db.SchoolYears
                .AsNoTracking()
                .OrderByDescending(y => y.StartDate)
                .Select(y => new { Year = y, Count = db.Semesters.Count(s => s.SchoolYearId == y.Id) })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                count = years.Count,
                schoolYears = years.Select(x => SchoolYearDto.From(x.Year, x.Count)).ToList()
            });
        }

        private static async Task<IResult> CreateAsync(
            SchoolYearRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            if (!Validate(request, out var name, out var start, out var end, out var problem))
            {
                return problem;
            }

            var year = new SchoolYear { Name = name, StartDate = start, EndDate = end };
            db.SchoolYears.Add(year);
            audit.Record(AuditAction.SchoolYearSaved, $"Created school year “{year.Name}”.",
                "SchoolYear", year.Id.ToString());
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/school-years/{year.Id}", SchoolYearDto.From(year, 0));
        }

        private static async Task<IResult> UpdateAsync(
            Guid id, SchoolYearRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var year = await db.SchoolYears.FirstOrDefaultAsync(y => y.Id == id, ct);
            if (year is null) return Results.NotFound(new { message = "School year not found." });

            if (!Validate(request, out var name, out var start, out var end, out var problem))
            {
                return problem;
            }

            year.Name = name;
            year.StartDate = start;
            year.EndDate = end;

            audit.Record(AuditAction.SchoolYearSaved, $"Updated school year “{year.Name}”.",
                "SchoolYear", year.Id.ToString());
            await db.SaveChangesAsync(ct);

            var count = await db.Semesters.CountAsync(s => s.SchoolYearId == year.Id, ct);
            return Results.Ok(SchoolYearDto.From(year, count));
        }

        private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var year = await db.SchoolYears.FirstOrDefaultAsync(y => y.Id == id, ct);
            if (year is null) return Results.NotFound(new { message = "School year not found." });

            if (await db.Semesters.AnyAsync(s => s.SchoolYearId == id, ct))
            {
                return Results.Conflict(new { message = "This school year still has semesters. Move or delete them first." });
            }

            db.SchoolYears.Remove(year);
            audit.Record(AuditAction.SchoolYearSaved, $"Deleted school year “{year.Name}”.",
                "SchoolYear", year.Id.ToString());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }

        private static async Task<IResult> SetActiveAsync(Guid id, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var year = await db.SchoolYears.FirstOrDefaultAsync(y => y.Id == id, ct);
            if (year is null) return Results.NotFound(new { message = "School year not found." });

            // Single active school year: clear the flag everywhere, then set it here.
            await db.SchoolYears.Where(y => y.IsActive && y.Id != id).ExecuteUpdateAsync(
                s => s.SetProperty(y => y.IsActive, false), ct);

            if (!year.IsActive)
            {
                year.IsActive = true;
                audit.Record(AuditAction.SchoolYearSaved, $"Set “{year.Name}” as the active school year.",
                    "SchoolYear", year.Id.ToString());
            }
            await db.SaveChangesAsync(ct);

            var count = await db.Semesters.CountAsync(s => s.SchoolYearId == year.Id, ct);
            return Results.Ok(SchoolYearDto.From(year, count));
        }

        private static bool Validate(
            SchoolYearRequest request, out string name, out DateOnly start, out DateOnly end, out IResult problem)
        {
            name = request.Name?.Trim() ?? string.Empty;
            start = default;
            end = default;
            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(name)) errors["name"] = ["A school year name is required."];

            var s = IsoDate.TryParse(request.StartDate);
            var e = IsoDate.TryParse(request.EndDate);
            if (s is null) errors["startDate"] = ["A valid start date is required."];
            if (e is null) errors["endDate"] = ["A valid end date is required."];
            if (s is { } sv && e is { } ev && ev < sv) errors["endDate"] = ["The end date must be on or after the start date."];

            if (errors.Count > 0)
            {
                problem = Results.ValidationProblem(errors);
                return false;
            }

            start = s!.Value;
            end = e!.Value;
            problem = Results.Empty;
            return true;
        }
    }
}
