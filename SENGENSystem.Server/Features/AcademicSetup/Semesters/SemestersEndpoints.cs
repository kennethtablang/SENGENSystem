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
    // The semester is one of the two hard-coded terms of its school year; its name is derived.
    public record SemesterRequest(Guid? SchoolYearId, string? Term, string? StartDate, string? EndDate);

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
            group.MapPost("/{id:guid}/archive", ArchiveAsync);
            group.MapPost("/{id:guid}/unarchive", UnarchiveAsync);
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
            var (ok, year, term, start, end, problem) = await ValidateAsync(request, null, db, ct);
            if (!ok) return problem;

            var semester = new Semester
            {
                Name = SemesterName(year!, term),
                Term = term,
                SchoolYearId = year!.Id,
                StartDate = start,
                EndDate = end
            };
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

            var (ok, year, term, start, end, problem) = await ValidateAsync(request, id, db, ct);
            if (!ok) return problem;

            semester.Name = SemesterName(year!, term);
            semester.Term = term;
            semester.SchoolYearId = year!.Id;
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
                || await db.TermActivations.AnyAsync(x => x.SemesterId == id, ct)
                || await db.FacultyLoadAssignments.AnyAsync(x => x.SemesterId == id, ct);
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

        // POST /api/semesters/{id}/archive — freeze a finished term: its set schedule becomes a
        // read-only archive (the board, generator, and publisher refuse changes) while every
        // row stays queryable for reports and history.
        private static async Task<IResult> ArchiveAsync(Guid id, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var semester = await db.Semesters.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (semester is null) return Results.NotFound(new { message = "Semester not found." });
            if (semester.IsActive)
            {
                return Results.Conflict(new { message = "The active semester can't be archived. Activate the next term first." });
            }
            if (semester.IsArchived)
            {
                return Results.Conflict(new { message = $"“{semester.Name}” is already archived." });
            }

            semester.IsArchived = true;
            semester.ArchivedAtUtc = DateTime.UtcNow;

            var scheduled = await db.ScheduleAssignments.CountAsync(a => a.SemesterId == id, ct);
            audit.Record(AuditAction.ScheduleArchived,
                $"Archived “{semester.Name}” — its schedule ({scheduled} placements) is now frozen and read-only.",
                "Semester", semester.Id.ToString());
            await db.SaveChangesAsync(ct);

            await db.Entry(semester).Reference(s => s.SchoolYear).LoadAsync(ct);
            return Results.Ok(SemesterDto.From(semester));
        }

        // POST /api/semesters/{id}/unarchive — reopen a term archived by mistake.
        private static async Task<IResult> UnarchiveAsync(Guid id, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var semester = await db.Semesters.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (semester is null) return Results.NotFound(new { message = "Semester not found." });
            if (!semester.IsArchived) return Results.Conflict(new { message = $"“{semester.Name}” is not archived." });

            semester.IsArchived = false;
            semester.ArchivedAtUtc = null;

            audit.Record(AuditAction.ScheduleArchived,
                $"Reopened “{semester.Name}” — its schedule can be edited again.",
                "Semester", semester.Id.ToString());
            await db.SaveChangesAsync(ct);

            await db.Entry(semester).Reference(s => s.SchoolYear).LoadAsync(ct);
            return Results.Ok(SemesterDto.From(semester));
        }

        private static async Task<(bool Ok, SchoolYear? Year, SemesterTerm Term, DateOnly Start, DateOnly End, IResult Problem)>
            ValidateAsync(SemesterRequest request, Guid? excludeId, AppDbContext db, CancellationToken ct)
        {
            var errors = new Dictionary<string, string[]>();

            SchoolYear? year = null;
            if (request.SchoolYearId is not { } yid)
            {
                errors["schoolYearId"] = ["Please choose a school year."];
            }
            else
            {
                year = await db.SchoolYears.FirstOrDefaultAsync(y => y.Id == yid, ct);
                if (year is null) errors["schoolYearId"] = ["The selected school year no longer exists."];
            }

            var termOk = Enum.TryParse<SemesterTerm>(request.Term, ignoreCase: true, out var term) && Enum.IsDefined(term);
            if (!termOk) errors["term"] = ["Please choose the semester (first or second)."];

            var s = IsoDate.TryParse(request.StartDate);
            var e = IsoDate.TryParse(request.EndDate);
            if (s is null) errors["startDate"] = ["A valid start date is required."];
            if (e is null) errors["endDate"] = ["A valid end date is required."];
            if (s is { } sv && e is { } ev && ev < sv) errors["endDate"] = ["The end date must be on or after the start date."];

            // A school year has at most one First and one Second semester.
            if (year is not null && termOk
                && await db.Semesters.AnyAsync(x => x.SchoolYearId == year.Id && x.Term == term && x.Id != excludeId, ct))
            {
                errors["term"] = [$"{year.Name} already has a {TermLabel(term)}."];
            }

            if (errors.Count > 0)
            {
                return (false, null, default, default, default, Results.ValidationProblem(errors));
            }

            return (true, year, term, s!.Value, e!.Value, Results.Empty);
        }

        private static string SemesterName(SchoolYear year, SemesterTerm term) => $"{year.Name} — {TermLabel(term)}";

        private static string TermLabel(SemesterTerm term) =>
            term == SemesterTerm.SecondSemester ? "Second Semester" : "First Semester";
    }
}
