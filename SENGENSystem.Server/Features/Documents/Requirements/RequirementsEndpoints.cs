using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Documents.Requirements
{
    // Vertical slice: the configurable admission-requirement catalog (FR-DOC-01). School personnel
    // add, rename, and archive requirements on the requirements page and choose which programs
    // (courses) each one applies to — so, for example, ITP enrollees skip the health papers only
    // HRS/HRA need. New SIS submissions seed their checklist from the active requirements matching
    // the enrollee's program (see DocumentChecklist.SeedDocuments).
    public record RequirementDto(
        Guid Id,
        string Code,
        string Name,
        string? Description,
        bool IsActive,
        int SortOrder,
        IReadOnlyList<string> Programs)
    {
        public static RequirementDto From(AdmissionRequirement r) =>
            new(r.Id, r.Code, r.Name, r.Description, r.IsActive, r.SortOrder,
                r.Programs.Select(p => p.Program.ToString()).OrderBy(p => p).ToList());
    }

    public record SaveRequirementRequest(string? Name, string? Description, bool? IsActive, string[]? Programs);

    public static class RequirementsEndpoints
    {
        public static IEndpointRouteBuilder MapRequirements(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/requirements")
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.AdmissionOfficer), nameof(UserRole.Registrar), nameof(UserRole.SchoolAdmin)));

            group.MapGet("", ListAsync);
            group.MapPost("", CreateAsync);
            group.MapPut("{id:guid}", UpdateAsync);
            group.MapDelete("{id:guid}", ArchiveAsync);
            return app;
        }

        private static async Task<IResult> ListAsync(AppDbContext db, CancellationToken ct)
        {
            var requirements = await db.AdmissionRequirements.AsNoTracking()
                .Include(r => r.Programs)
                .OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
                .ToListAsync(ct);

            return Results.Ok(new { requirements = requirements.Select(RequirementDto.From).ToList() });
        }

        private static async Task<IResult> CreateAsync(
            SaveRequirementRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            if (!TryParse(request, out var name, out var programs, out var problem))
            {
                return problem;
            }

            var maxOrder = await db.AdmissionRequirements.Select(r => (int?)r.SortOrder).MaxAsync(ct) ?? 0;
            var requirement = new AdmissionRequirement
            {
                Code = await GenerateCodeAsync(db, name, ct),
                Name = name,
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                IsActive = request.IsActive ?? true,
                SortOrder = maxOrder + 1,
                Programs = programs.Select(p => new AdmissionRequirementProgram { Program = p }).ToList()
            };

            db.AdmissionRequirements.Add(requirement);
            audit.Record(AuditAction.RequirementCreated,
                $"Added admission requirement \"{name}\" for {ProgramList(programs)}.",
                "AdmissionRequirement", requirement.Id.ToString());
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/requirements/{requirement.Id}", RequirementDto.From(requirement));
        }

        private static async Task<IResult> UpdateAsync(
            Guid id, SaveRequirementRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var requirement = await db.AdmissionRequirements
                .Include(r => r.Programs)
                .FirstOrDefaultAsync(r => r.Id == id, ct);
            if (requirement is null)
            {
                return Results.NotFound(new { message = "Requirement not found." });
            }

            if (!TryParse(request, out var name, out var programs, out var problem))
            {
                return problem;
            }

            requirement.Name = name;
            requirement.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            if (request.IsActive is { } active) requirement.IsActive = active;

            // Reconcile the program set.
            requirement.Programs.Clear();
            foreach (var p in programs)
            {
                requirement.Programs.Add(new AdmissionRequirementProgram { Program = p });
            }

            audit.Record(AuditAction.RequirementUpdated,
                $"Updated admission requirement \"{name}\" — applies to {ProgramList(programs)}" +
                $"{(requirement.IsActive ? string.Empty : " (archived)")}.",
                "AdmissionRequirement", requirement.Id.ToString());
            await db.SaveChangesAsync(ct);

            return Results.Ok(RequirementDto.From(requirement));
        }

        // Soft-delete: archived requirements stop seeding onto new checklists but historical
        // RegistrationDocument rows keep a resolvable label.
        private static async Task<IResult> ArchiveAsync(
            Guid id, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var requirement = await db.AdmissionRequirements.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (requirement is null)
            {
                return Results.NotFound(new { message = "Requirement not found." });
            }
            if (!requirement.IsActive)
            {
                return Results.Ok(new { archived = true });
            }

            requirement.IsActive = false;
            audit.Record(AuditAction.RequirementArchived,
                $"Archived admission requirement \"{requirement.Name}\".",
                "AdmissionRequirement", requirement.Id.ToString());
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { archived = true });
        }

        private static bool TryParse(
            SaveRequirementRequest request, out string name, out List<ProgramTrack> programs, out IResult problem)
        {
            name = request.Name?.Trim() ?? string.Empty;
            programs = new List<ProgramTrack>();
            problem = Results.Empty;

            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(name))
            {
                errors["name"] = ["A requirement name is required."];
            }

            foreach (var raw in request.Programs ?? [])
            {
                if (Enum.TryParse<ProgramTrack>(raw, ignoreCase: true, out var p) && Enum.IsDefined(p))
                {
                    if (!programs.Contains(p)) programs.Add(p);
                }
                else
                {
                    errors["programs"] = ["One or more selected programs are invalid."];
                    break;
                }
            }

            if (programs.Count == 0 && !errors.ContainsKey("programs"))
            {
                errors["programs"] = ["Choose at least one program this requirement applies to."];
            }

            if (errors.Count > 0)
            {
                problem = Results.ValidationProblem(errors);
                return false;
            }
            return true;
        }

        /// <summary>Derives a stable, unique PascalCase code from the requirement name.</summary>
        private static async Task<string> GenerateCodeAsync(AppDbContext db, string name, CancellationToken ct)
        {
            var letters = new string(name.Where(char.IsLetterOrDigit).ToArray());
            var baseCode = string.IsNullOrEmpty(letters) ? "Requirement" : letters;
            if (baseCode.Length > 30) baseCode = baseCode[..30];

            var code = baseCode;
            var suffix = 1;
            while (await db.AdmissionRequirements.AnyAsync(r => r.Code == code, ct))
            {
                code = $"{baseCode}{++suffix}";
            }
            return code;
        }

        private static string ProgramList(IEnumerable<ProgramTrack> programs) =>
            string.Join(", ", programs.Select(p => p.ToString()));
    }
}
