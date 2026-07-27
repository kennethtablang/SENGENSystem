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
        IReadOnlyList<string> Programs,
        IReadOnlyList<string> StudentTypes,
        bool IsRequiredForAuthorization,
        bool AcceptsCertificateOfGrades)
    {
        public static RequirementDto From(AdmissionRequirement r) =>
            new(r.Id, r.Code, r.Name, r.Description, r.IsActive, r.SortOrder,
                r.Programs.Select(p => p.Program.ToString()).OrderBy(p => p).ToList(),
                StudentTypesOf(r),
                r.IsRequiredForAuthorization,
                r.AcceptsCertificateOfGrades);

        private static List<string> StudentTypesOf(AdmissionRequirement r)
        {
            var types = new List<string>();
            if (r.AppliesToNewStudents) types.Add(nameof(StudentType.NewStudent));
            if (r.AppliesToTransferees) types.Add(nameof(StudentType.Transferee));
            return types;
        }
    }

    public record SaveRequirementRequest(
        string? Name,
        string? Description,
        bool? IsActive,
        string[]? Programs,
        string[]? StudentTypes,
        bool? IsRequiredForAuthorization,
        bool? AcceptsCertificateOfGrades);

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
            if (!TryParse(request, out var parsed, out var problem))
            {
                return problem;
            }

            var maxOrder = await db.AdmissionRequirements.Select(r => (int?)r.SortOrder).MaxAsync(ct) ?? 0;
            var requirement = new AdmissionRequirement
            {
                Code = await GenerateCodeAsync(db, parsed.Name, ct),
                Name = parsed.Name,
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                IsActive = request.IsActive ?? true,
                SortOrder = maxOrder + 1,
                AppliesToNewStudents = parsed.NewStudents,
                AppliesToTransferees = parsed.Transferees,
                IsRequiredForAuthorization = request.IsRequiredForAuthorization ?? false,
                AcceptsCertificateOfGrades = request.AcceptsCertificateOfGrades ?? false,
                Programs = parsed.Programs.Select(p => new AdmissionRequirementProgram { Program = p }).ToList()
            };

            db.AdmissionRequirements.Add(requirement);
            audit.Record(AuditAction.RequirementCreated,
                $"Added admission requirement \"{parsed.Name}\" for {ProgramList(parsed.Programs)} " +
                $"({StudentTypeList(parsed)}){(requirement.IsRequiredForAuthorization ? ", required for authorization" : string.Empty)}.",
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

            if (!TryParse(request, out var parsed, out var problem))
            {
                return problem;
            }

            requirement.Name = parsed.Name;
            requirement.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            if (request.IsActive is { } active) requirement.IsActive = active;
            requirement.AppliesToNewStudents = parsed.NewStudents;
            requirement.AppliesToTransferees = parsed.Transferees;
            if (request.IsRequiredForAuthorization is { } gating) requirement.IsRequiredForAuthorization = gating;
            if (request.AcceptsCertificateOfGrades is { } cog) requirement.AcceptsCertificateOfGrades = cog;

            // Reconcile the program set.
            requirement.Programs.Clear();
            foreach (var p in parsed.Programs)
            {
                requirement.Programs.Add(new AdmissionRequirementProgram { Program = p });
            }

            audit.Record(AuditAction.RequirementUpdated,
                $"Updated admission requirement \"{parsed.Name}\" — applies to {ProgramList(parsed.Programs)} " +
                $"({StudentTypeList(parsed)})" +
                $"{(requirement.IsRequiredForAuthorization ? ", required for authorization" : string.Empty)}" +
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

        /// <summary>
        /// A validated save request: the name, the programs the paper applies to, and the student
        /// types that are asked for it. Omitting <c>StudentTypes</c> keeps the historical
        /// "everyone" behaviour, so an older client cannot silently narrow a requirement.
        /// </summary>
        private sealed record ParsedRequirement(
            string Name, List<ProgramTrack> Programs, bool NewStudents, bool Transferees);

        private static bool TryParse(
            SaveRequirementRequest request, out ParsedRequirement parsed, out IResult problem)
        {
            var name = request.Name?.Trim() ?? string.Empty;
            var programs = new List<ProgramTrack>();
            var newStudents = request.StudentTypes is null;
            var transferees = request.StudentTypes is null;
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

            foreach (var raw in request.StudentTypes ?? [])
            {
                if (Enum.TryParse<StudentType>(raw, ignoreCase: true, out var t) && Enum.IsDefined(t))
                {
                    if (t == StudentType.Transferee) transferees = true; else newStudents = true;
                }
                else
                {
                    errors["studentTypes"] = ["One or more selected student types are invalid."];
                    break;
                }
            }

            if (!newStudents && !transferees && !errors.ContainsKey("studentTypes"))
            {
                errors["studentTypes"] = ["Choose at least one student type this requirement applies to."];
            }

            parsed = new ParsedRequirement(name, programs, newStudents, transferees);
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

        private static string StudentTypeList(ParsedRequirement parsed) =>
            parsed.NewStudents && parsed.Transferees ? "all student types"
            : parsed.Transferees ? "transferees only"
            : "new students only";
    }
}
