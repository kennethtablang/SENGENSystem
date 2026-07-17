using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Curriculum.Subjects
{
    // Vertical slice: the Academic Head manages the subjects within a curriculum (FR-SCHED-04/05).
    // Subjects carry units, year level, a laboratory flag, and prerequisites (other subjects in the
    // same curriculum). A subject already used by a section can't be deleted.
    public record SubjectRequest(
        Guid? CurriculumId,
        string? Code,
        string? Title,
        int? Units,
        int? Hours,
        int? YearLevel,
        string? Term,
        bool RequiresLaboratory,
        List<Guid>? PrerequisiteSubjectIds);

    public record ArchiveSubjectRequest(string? Reason);

    public static class SubjectsEndpoints
    {
        public static IEndpointRouteBuilder MapSubjects(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/subjects")
                .RequireAuthorization(policy =>
                    policy.RequireRole(nameof(UserRole.AcademicHead), nameof(UserRole.SchoolAdmin)));

            group.MapGet("", ListAsync);
            group.MapPost("", CreateAsync);
            group.MapPut("/{id:guid}", UpdateAsync);
            group.MapDelete("/{id:guid}", DeleteAsync);
            group.MapPost("/{id:guid}/archive", ArchiveAsync);
            group.MapPost("/{id:guid}/restore", RestoreAsync);
            return app;
        }

        private static async Task<IResult> ListAsync(Guid? curriculumId, AppDbContext db, CancellationToken ct)
        {
            var query = db.Subjects.AsNoTracking()
                .Include(s => s.Prerequisites).ThenInclude(p => p.PrerequisiteSubject)
                .AsQueryable();
            if (curriculumId is { } cid) query = query.Where(s => s.CurriculumId == cid);

            var subjects = await query
                .OrderBy(s => s.YearLevel).ThenBy(s => s.Code)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                count = subjects.Count,
                subjects = subjects.Select(SubjectDto.From).ToList()
            });
        }

        private static async Task<IResult> CreateAsync(
            SubjectRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var (ok, code, title, units, hours, year, term, curriculum, problem) = await ValidateAsync(request, null, db, ct);
            if (!ok) return problem;

            var subject = new Subject
            {
                CurriculumId = curriculum!.Id,
                ProgramCode = curriculum.ProgramCode,
                Code = code,
                Title = title,
                Units = units,
                Hours = hours,
                YearLevel = year,
                Term = term,
                RequiresLaboratory = request.RequiresLaboratory
            };
            db.Subjects.Add(subject);

            await ReconcilePrerequisitesAsync(db, subject.Id, curriculum.Id, request.PrerequisiteSubjectIds, ct);

            audit.Record(AuditAction.SubjectSaved, $"Added subject {subject.Code} — {subject.Title}.",
                "Subject", subject.Id.ToString());
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/subjects/{subject.Id}", await LoadDtoAsync(db, subject.Id, ct));
        }

        private static async Task<IResult> UpdateAsync(
            Guid id, SubjectRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var subject = await db.Subjects.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (subject is null) return Results.NotFound(new { message = "Subject not found." });

            var (ok, code, title, units, hours, year, term, curriculum, problem) = await ValidateAsync(request, id, db, ct);
            if (!ok) return problem;

            subject.CurriculumId = curriculum!.Id;
            subject.ProgramCode = curriculum.ProgramCode;
            subject.Code = code;
            subject.Title = title;
            subject.Units = units;
            subject.Hours = hours;
            subject.YearLevel = year;
            subject.Term = term;
            subject.RequiresLaboratory = request.RequiresLaboratory;

            await ReconcilePrerequisitesAsync(db, subject.Id, curriculum.Id, request.PrerequisiteSubjectIds, ct);

            audit.Record(AuditAction.SubjectSaved, $"Updated subject {subject.Code} — {subject.Title}.",
                "Subject", subject.Id.ToString());
            await db.SaveChangesAsync(ct);

            return Results.Ok(await LoadDtoAsync(db, subject.Id, ct));
        }

        private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var subject = await db.Subjects.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (subject is null) return Results.NotFound(new { message = "Subject not found." });

            if (await db.Sections.AnyAsync(s => s.SubjectId == id, ct))
            {
                return Results.Conflict(new
                {
                    message = "This subject is used by a section and can't be deleted. Archive it instead — " +
                        "archived subjects keep their history but leave the active curriculum."
                });
            }

            // Remove edges where this subject is required by others (the Restrict FK side), then the
            // subject itself (its own prerequisite rows cascade away).
            var requiredBy = await db.SubjectPrerequisites.Where(p => p.PrerequisiteSubjectId == id).ToListAsync(ct);
            db.SubjectPrerequisites.RemoveRange(requiredBy);
            db.Subjects.Remove(subject);

            audit.Record(AuditAction.SubjectSaved, $"Deleted subject {subject.Code} — {subject.Title}.",
                "Subject", subject.Id.ToString());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }

        // POST /api/subjects/{id}/archive — soft-retire a subject when the curriculum changes.
        // History (sections, loads, schedules) is untouched; the subject just stops being offered.
        private static async Task<IResult> ArchiveAsync(
            Guid id, ArchiveSubjectRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var subject = await db.Subjects.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (subject is null) return Results.NotFound(new { message = "Subject not found." });
            if (subject.IsArchived) return Results.Conflict(new { message = $"{subject.Code} is already archived." });

            subject.IsArchived = true;
            subject.ArchivedAtUtc = DateTime.UtcNow;
            subject.ArchiveReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();

            audit.Record(AuditAction.SubjectArchived,
                $"Archived subject {subject.Code} — {subject.Title}" +
                (subject.ArchiveReason is null ? "." : $" ({subject.ArchiveReason})."),
                "Subject", subject.Id.ToString());
            await db.SaveChangesAsync(ct);

            return Results.Ok(await LoadDtoAsync(db, subject.Id, ct));
        }

        // POST /api/subjects/{id}/restore — bring an archived subject back into the curriculum.
        private static async Task<IResult> RestoreAsync(Guid id, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var subject = await db.Subjects.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (subject is null) return Results.NotFound(new { message = "Subject not found." });
            if (!subject.IsArchived) return Results.Conflict(new { message = $"{subject.Code} is not archived." });

            subject.IsArchived = false;
            subject.ArchivedAtUtc = null;
            subject.ArchiveReason = null;

            audit.Record(AuditAction.SubjectRestored,
                $"Restored subject {subject.Code} — {subject.Title} from the archive.",
                "Subject", subject.Id.ToString());
            await db.SaveChangesAsync(ct);

            return Results.Ok(await LoadDtoAsync(db, subject.Id, ct));
        }

        /// <summary>Makes the stored prerequisite edges for a subject match the requested set.</summary>
        private static async Task ReconcilePrerequisitesAsync(
            AppDbContext db, Guid subjectId, Guid curriculumId, List<Guid>? requested, CancellationToken ct)
        {
            // Only same-curriculum subjects, never the subject itself.
            var desired = requested is null || requested.Count == 0
                ? new List<Guid>()
                : await db.Subjects
                    .Where(s => requested.Contains(s.Id) && s.CurriculumId == curriculumId && s.Id != subjectId)
                    .Select(s => s.Id)
                    .ToListAsync(ct);

            var existing = await db.SubjectPrerequisites.Where(p => p.SubjectId == subjectId).ToListAsync(ct);

            foreach (var edge in existing.Where(e => !desired.Contains(e.PrerequisiteSubjectId)))
            {
                db.SubjectPrerequisites.Remove(edge);
            }
            foreach (var addId in desired.Where(d => existing.All(e => e.PrerequisiteSubjectId != d)))
            {
                db.SubjectPrerequisites.Add(new SubjectPrerequisite { SubjectId = subjectId, PrerequisiteSubjectId = addId });
            }
        }

        private static async Task<SubjectDto> LoadDtoAsync(AppDbContext db, Guid id, CancellationToken ct)
        {
            var subject = await db.Subjects.AsNoTracking()
                .Include(s => s.Prerequisites).ThenInclude(p => p.PrerequisiteSubject)
                .FirstAsync(s => s.Id == id, ct);
            return SubjectDto.From(subject);
        }

        private static async Task<(bool Ok, string Code, string Title, int Units, int Hours, int Year, SemesterTerm Term, Domain.Curriculum? Curriculum, IResult Problem)>
            ValidateAsync(SubjectRequest request, Guid? subjectId, AppDbContext db, CancellationToken ct)
        {
            var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
            var title = request.Title?.Trim() ?? string.Empty;
            var errors = new Dictionary<string, string[]>();

            if (!Enum.TryParse<SemesterTerm>(request.Term, ignoreCase: true, out var term) || !Enum.IsDefined(term))
            {
                errors["term"] = ["Please choose the term this subject is offered in."];
            }

            Domain.Curriculum? curriculum = null;
            if (request.CurriculumId is not { } cid)
            {
                errors["curriculumId"] = ["Please choose a curriculum."];
            }
            else
            {
                curriculum = await db.Curricula.FirstOrDefaultAsync(c => c.Id == cid, ct);
                if (curriculum is null) errors["curriculumId"] = ["The selected curriculum no longer exists."];
            }

            if (string.IsNullOrWhiteSpace(code)) errors["code"] = ["A subject code is required."];
            if (string.IsNullOrWhiteSpace(title)) errors["title"] = ["A subject title is required."];

            if (request.Units is not { } units || units is < 1 or > 20)
            {
                errors["units"] = ["Units must be between 1 and 20."];
            }
            if (request.Hours is not { } hours || hours is < 1 or > 40)
            {
                errors["hours"] = ["Weekly hours must be between 1 and 40."];
            }
            if (request.YearLevel is not { } year || year is < 1 or > 3)
            {
                errors["yearLevel"] = ["Year level must be between 1 and 3."];
            }

            // Code is unique within the curriculum.
            if (curriculum is not null && !string.IsNullOrWhiteSpace(code)
                && await db.Subjects.AnyAsync(s => s.CurriculumId == curriculum.Id && s.Code == code && s.Id != subjectId, ct))
            {
                errors["code"] = [$"{code} already exists in this curriculum."];
            }

            if (errors.Count > 0)
            {
                return (false, code, title, 0, 0, 0, default, null, Results.ValidationProblem(errors));
            }

            return (true, code, title, request.Units!.Value, request.Hours!.Value, request.YearLevel!.Value, term, curriculum, Results.Empty);
        }
    }
}
