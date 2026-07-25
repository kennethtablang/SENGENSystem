using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Documents
{
    /// <summary>
    /// Shared checklist semantics (FR-DOC): a checklist is complete when every tracked paper
    /// has been received in some form — original or photocopy (any status except NotSubmitted).
    /// Requirement labels and ordering now come from the configurable requirement catalog
    /// (<see cref="AdmissionRequirement"/>), resolved through a <see cref="RequirementCatalog"/>.
    /// </summary>
    internal static class DocumentChecklist
    {
        public static bool IsComplete(IEnumerable<RegistrationDocument> documents) =>
            documents.All(d => d.Status != DocumentStatus.NotSubmitted);

        public static int SubmittedCount(IEnumerable<RegistrationDocument> documents) =>
            documents.Count(d => d.Status != DocumentStatus.NotSubmitted);

        /// <summary>Loads the requirement catalog once so labels/order can be resolved per code.</summary>
        public static async Task<RequirementCatalog> LoadCatalogAsync(AppDbContext db, CancellationToken ct)
        {
            var entries = await db.AdmissionRequirements.AsNoTracking()
                .Select(r => new RequirementEntry(r.Code, r.Name, r.SortOrder))
                .ToListAsync(ct);
            return new RequirementCatalog(entries);
        }

        /// <summary>
        /// Loads the active requirements (with their program applicability) used to seed a fresh
        /// checklist. Load once and reuse across many registrations when importing in bulk.
        /// </summary>
        public static async Task<List<AdmissionRequirement>> LoadActiveRequirementsAsync(
            AppDbContext db, CancellationToken ct) =>
            await db.AdmissionRequirements.AsNoTracking()
                .Where(r => r.IsActive)
                .Include(r => r.Programs)
                .OrderBy(r => r.SortOrder)
                .ToListAsync(ct);

        /// <summary>
        /// Seeds the admission-requirements checklist for a new registration: one document row per
        /// active requirement whose program set includes the enrollee's program (FR-DOC-01). A
        /// student is therefore only asked for the papers their course requires.
        /// </summary>
        public static void SeedDocuments(
            StudentRegistration registration, IEnumerable<AdmissionRequirement> activeRequirements)
        {
            foreach (var requirement in activeRequirements)
            {
                if (requirement.Programs.Any(p => p.Program == registration.Program))
                {
                    registration.Documents.Add(new RegistrationDocument { RequirementCode = requirement.Code });
                }
            }
        }

        /// <summary>Title-cases a bare requirement code as a fallback when it is not in the catalog.</summary>
        internal static string Humanize(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return code;
            var spaced = System.Text.RegularExpressions.Regex.Replace(code, "([a-z0-9])([A-Z])", "$1 $2");
            return char.ToUpperInvariant(spaced[0]) + spaced[1..];
        }
    }

    internal sealed record RequirementEntry(string Code, string Name, int SortOrder);

    /// <summary>A code → display-name / sort-order lookup for the active requirement catalog.</summary>
    public sealed class RequirementCatalog
    {
        private readonly Dictionary<string, RequirementEntry> _byCode;

        internal RequirementCatalog(IEnumerable<RequirementEntry> entries) =>
            _byCode = entries.ToDictionary(e => e.Code, StringComparer.OrdinalIgnoreCase);

        /// <summary>Display label for a requirement code; falls back to the humanized code.</summary>
        public string Label(string code) =>
            _byCode.TryGetValue(code, out var e) ? e.Name : DocumentChecklist.Humanize(code);

        /// <summary>Presentation order for a requirement code; unknown codes sort last.</summary>
        public int Order(string code) =>
            _byCode.TryGetValue(code, out var e) ? e.SortOrder : int.MaxValue;
    }
}
