using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.AcademicSetup.Semesters
{
    // Vertical slice: the School Admin manages semesters within a school year. The single active
    // semester is the term-aware source the dashboard and scheduling key off (FR-DASH, FR-SCHED).
    // Safe delete refuses to remove a semester referenced by sections, registrations, or term
    // activations (409).
    public record SemesterRequest(string? Name, Guid? SchoolYearId, string? StartDate, string? EndDate);

    public static class SemestersEndpoints
    {
        public static IEndpointRouteBuilder MapSemesters(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/semesters")
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.SchoolAdmin)));

            group.MapGet("", ListAsync);
            group.MapPost("", CreateAsync);
            group.MapPut("/{id:guid}", UpdateAsync);
            group.MapDelete("/{id:guid}", DeleteAsync);
            group.MapPost("/{id:guid}/active", SetActiveAsync);
            return app;
        }

        private static async Task<IResult> ListAsync(Guid? schoolYearId, AppDbContext db, CancellationToken ct)
        {
            var query = db.Semesters.AsNoTracking().Include(s => s.SchoolYear).AsQueryable();
            if (schoolYearId is { } yid) query = query.Where(s => s.SchoolYearId == yid);

            var semesters = await query
                .OrderByDescending(s => s.StartDate)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                count = semesters.Count,
                semesters = semesters.Select(SemesterDto.From).ToList()
            });
        }

        private static async Task<IResult> CreateAsync(
            SemesterRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var (ok, name, yearId, start, end, problem) = await ValidateAsync(request, db, ct);
            if (!ok) return problem;

            var semester = new Semester { Name = name, SchoolYearId = yearId, StartDate = start, EndDate = end };
            db.Semesters.Add(semester);
            audit.Record(AuditAction.SemesterSaved, $"Created semester “{semester.Name}”.",
                "Semester", semester.Id.ToString());
            await db.SaveChangesAsync(ct);

            await db.Entry(semester).Reference(s => s.SchoolYear).LoadAsync(ct);
            return Results.Created($"/api/semesters/{semester.Id}", SemesterDto.From(semester));
        }

        private static async Task<IResult> UpdateAsync(
            Guid id, SemesterRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var semester = await db.Semesters.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (semester is null) return Results.NotFound(new { message = "Semester not found." });

            var (ok, name, yearId, start, end, problem) = await ValidateAsync(request, db, ct);
            if (!ok) return problem;

            semester.Name = name;
            semester.SchoolYearId = yearId;
            semester.StartDate = start;
            semester.EndDate = end;

            audit.Record(AuditAction.SemesterSaved, $"Updated semester “{semester.Name}”.",
                "Semester", semester.Id.ToString());
            await db.SaveChangesAsync(ct);

            await db.Entry(semester).Reference(s => s.SchoolYear).LoadAsync(ct);
            return Results.Ok(SemesterDto.From(semester));
        }

        private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var semester = await db.Semesters.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (semester is null) return Results.NotFound(new { message = "Semester not found." });

            var referenced = await db.Sections.AnyAsync(x => x.SemesterId == id, ct)
                || await db.StudentRegistrations.AnyAsync(x => x.SemesterId == id, ct)
                || await db.TermActivations.AnyAsync(x => x.SemesterId == id, ct);
            if (referenced)
            {
                return Results.Conflict(new { message = "This semester is in use by sections, registrations, or term activations and can't be deleted." });
            }

            db.Semesters.Remove(semester);
            audit.Record(AuditAction.SemesterSaved, $"Deleted semester “{semester.Name}”.",
                "Semester", semester.Id.ToString());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }

        private static async Task<IResult> SetActiveAsync(Guid id, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var semester = await db.Semesters.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (semester is null) return Results.NotFound(new { message = "Semester not found." });

            // Single active semester across the institution — the term-aware source of truth.
            await db.Semesters.Where(s => s.IsActive && s.Id != id).ExecuteUpdateAsync(
                x => x.SetProperty(s => s.IsActive, false), ct);

            if (!semester.IsActive)
            {
                semester.IsActive = true;
                audit.Record(AuditAction.SemesterSaved, $"Set “{semester.Name}” as the active semester.",
                    "Semester", semester.Id.ToString());
            }
            await db.SaveChangesAsync(ct);

            await db.Entry(semester).Reference(s => s.SchoolYear).LoadAsync(ct);
            return Results.Ok(SemesterDto.From(semester));
        }

        private static async Task<(bool Ok, string Name, Guid? YearId, DateOnly Start, DateOnly End, IResult Problem)>
            ValidateAsync(SemesterRequest request, AppDbContext db, CancellationToken ct)
        {
            var name = request.Name?.Trim() ?? string.Empty;
            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(name)) errors["name"] = ["A semester name is required."];

            if (request.SchoolYearId is not { } yid)
            {
                errors["schoolYearId"] = ["Please choose a school year."];
            }
            else if (!await db.SchoolYears.AnyAsync(y => y.Id == yid, ct))
            {
                errors["schoolYearId"] = ["The selected school year no longer exists."];
            }

            var s = IsoDate.TryParse(request.StartDate);
            var e = IsoDate.TryParse(request.EndDate);
            if (s is null) errors["startDate"] = ["A valid start date is required."];
            if (e is null) errors["endDate"] = ["A valid end date is required."];
            if (s is { } sv && e is { } ev && ev < sv) errors["endDate"] = ["The end date must be on or after the start date."];

            if (errors.Count > 0)
            {
                return (false, name, null, default, default, Results.ValidationProblem(errors));
            }

            return (true, name, request.SchoolYearId, s!.Value, e!.Value, Results.Empty);
        }
    }
}
