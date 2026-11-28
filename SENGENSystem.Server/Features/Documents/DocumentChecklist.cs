using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Documents
{
    /// <summary>
    /// Shared checklist semantics (FR-DOC): a checklist is complete when every tracked paper
    /// has been received in some form — original, photocopy, or an accepted certificate of grades
    /// (any status except NotSubmitted). Requirement labels, ordering, applicability, and the
    /// authorization gate all come from the configurable requirement catalog
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
                .Select(r => new
                {
                    r.Code, r.Name, r.SortOrder,
                    r.IsRequiredForAuthorization, r.AcceptsCertificateOfGrades,
                    r.AppliesToNewStudents, r.AppliesToTransferees,
                    Programs = r.Programs.Select(p => p.Program).ToList()
                })
                .ToListAsync(ct);
            return new RequirementCatalog(entries.Select(r => new RequirementEntry(
                r.Code, r.Name, r.SortOrder,
                r.IsRequiredForAuthorization, r.AcceptsCertificateOfGrades,
                r.AppliesToNewStudents, r.AppliesToTransferees,
                r.Programs.ToHashSet())));
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
        /// active requirement that applies both to the enrollee's program and to their student type
        /// (FR-DOC-01). A student is therefore only asked for the papers their course requires and
        /// their route into the school can actually produce — a new enrollee is never asked for a
        /// transcript or honorable dismissal, a transferee never for a Form 138/137 or good moral.
        /// </summary>
        public static void SeedDocuments(
            StudentRegistration registration, IEnumerable<AdmissionRequirement> activeRequirements)
        {
            foreach (var requirement in activeRequirements)
            {
                if (requirement.Programs.Any(p => p.Program == registration.Program)
                    && AppliesTo(requirement, registration.StudentType))
                {
                    registration.Documents.Add(new RegistrationDocument { RequirementCode = requirement.Code });
                }
            }
        }

        /// <summary>
        /// Brings existing checklists in line with a requirement that was just added, re-activated,
        /// or widened to more programs / student types: every open registration it now applies to
        /// that has no row for it gets a fresh NotSubmitted one. Without this, a paper added on the
        /// Manage Requirements screen (an X-ray, say) only ever reached students who submitted
        /// their SIS afterwards. "Open" means not rejected and in a semester that is not archived —
        /// a closed term's records stay as they were. Rows are staged, not saved, so they commit
        /// together with the requirement change. Returns how many checklists gained the paper.
        /// </summary>
        public static async Task<int> BackfillAsync(
            AppDbContext db, AdmissionRequirement requirement, CancellationToken ct)
        {
            var programs = requirement.Programs.Select(p => p.Program).Distinct().ToList();
            if (!requirement.IsActive || programs.Count == 0) return 0;

            var code = requirement.Code;
            var newStudents = requirement.AppliesToNewStudents;
            var transferees = requirement.AppliesToTransferees;

            var registrationIds = await db.StudentRegistrations
                .Where(r => r.Status != RegistrationStatus.Rejected
                    && !r.Semester!.IsArchived
                    && programs.Contains(r.Program)
                    && ((transferees && r.StudentType == StudentType.Transferee)
                        || (newStudents && r.StudentType != StudentType.Transferee))
                    && !r.Documents.Any(d => d.RequirementCode == code))
                .Select(r => r.Id)
                .ToListAsync(ct);

            db.RegistrationDocuments.AddRange(registrationIds.Select(id =>
                new RegistrationDocument { StudentRegistrationId = id, RequirementCode = code }));
            return registrationIds.Count;
        }

        /// <summary>Whether a requirement is asked of the given student type.</summary>
        public static bool AppliesTo(AdmissionRequirement requirement, StudentType studentType) =>
            studentType == StudentType.Transferee
                ? requirement.AppliesToTransferees
                : requirement.AppliesToNewStudents;

        /// <summary>
        /// The checklist rows that actually apply to this enrollee. Seeding already filters by
        /// program and student type, but a checklist seeded before a requirement's applicability
        /// was narrowed still carries the row — this is what keeps a transferee's board free of the
        /// Form 138 they can never produce (or an ITP student's free of a paper taken off ITP), and
        /// keeps the counts honest with what is shown.
        /// </summary>
        public static List<RegistrationDocument> Applicable(
            StudentRegistration registration, RequirementCatalog catalog) =>
            registration.Documents
                .Where(d => catalog.AppliesTo(d.RequirementCode, registration.StudentType, registration.Program))
                .ToList();

        /// <summary>
        /// The gating papers (FR-PRE-02) still outstanding on a checklist: the requirements flagged
        /// <see cref="AdmissionRequirement.IsRequiredForAuthorization"/> whose row is still
        /// NotSubmitted. Empty means the Admission Officer may clear the student for enlistment;
        /// everything else on the checklist is followed up on rather than blocked on.
        /// </summary>
        public static List<string> MissingAuthorizationRequirements(
            IEnumerable<RegistrationDocument> documents, RequirementCatalog catalog) =>
            documents
                .Where(d => d.Status == DocumentStatus.NotSubmitted && catalog.GatesAuthorization(d.RequirementCode))
                .OrderBy(d => catalog.Order(d.RequirementCode))
                .Select(d => catalog.Label(d.RequirementCode))
                .ToList();

        /// <summary>
        /// Records a decision on one paper — the one place every path (the board, the Registrar's
        /// SIS correction, the spreadsheet import) changes a checklist row, so the status, who
        /// decided it and when (FR-DOC-03), and the date it was received always move together.
        /// <para>
        /// A received paper keeps the date given, else the one it already has when its form is
        /// unchanged, else today (Philippine time). Going back to not submitted clears the date.
        /// Returns whether anything changed.
        /// </para>
        /// </summary>
        public static bool Record(
            RegistrationDocument document, DocumentStatus status, DateOnly? submittedOn, Guid? decidedBy, DateTime nowUtc)
        {
            DateOnly? date = status == DocumentStatus.NotSubmitted
                ? null
                : submittedOn ?? (document.Status == status ? document.SubmittedOn : null) ?? Today();
            if (document.Status == status && document.SubmittedOn == date) return false;

            document.Status = status;
            document.SubmittedOn = date;
            document.UpdatedAtUtc = nowUtc;
            document.VerifiedByUserId = decidedBy;
            return true;
        }

        /// <summary>Today in the institution's time zone — a paper handed in at 7 a.m. PHT is not "yesterday".</summary>
        public static DateOnly Today() => DateOnly.FromDateTime(SENGENSystem.Server.Common.InstitutionClock.Now);

        /// <summary>
        /// Why a received-on date cannot be recorded, or null when it can: never in the future, and
        /// not absurdly old (a mistyped year).
        /// </summary>
        public static string? SubmittedOnProblem(DateOnly date) =>
            date > Today() ? "The date received cannot be in the future."
            : date.Year < 2000 ? "The date received looks mistyped — check the year."
            : null;

        /// <summary>Title-cases a bare requirement code as a fallback when it is not in the catalog.</summary>
        internal static string Humanize(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return code;
            var spaced = System.Text.RegularExpressions.Regex.Replace(code, "([a-z0-9])([A-Z])", "$1 $2");
            return char.ToUpperInvariant(spaced[0]) + spaced[1..];
        }
    }

    internal sealed record RequirementEntry(
        string Code,
        string Name,
        int SortOrder,
        bool IsRequiredForAuthorization,
        bool AcceptsCertificateOfGrades,
        bool AppliesToNewStudents,
        bool AppliesToTransferees,
        IReadOnlySet<ProgramTrack> Programs);

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

        /// <summary>Whether this paper must be in hand before the student can be pre-authorized.</summary>
        public bool GatesAuthorization(string code) =>
            _byCode.TryGetValue(code, out var e) && e.IsRequiredForAuthorization;

        /// <summary>
        /// Whether a Certificate of Grades stands in for this paper. Such a requirement offers
        /// that status instead of "Xerox copy" — the two are never both on offer.
        /// </summary>
        public bool AcceptsCertificateOfGrades(string code) =>
            _byCode.TryGetValue(code, out var e) && e.AcceptsCertificateOfGrades;

        /// <summary>The statuses the checklist may record against this paper.</summary>
        public IReadOnlyList<DocumentStatus> StatusesFor(string code) =>
            AcceptsCertificateOfGrades(code)
                ? [DocumentStatus.NotSubmitted, DocumentStatus.Submitted, DocumentStatus.CertificateOfGrades]
                : [DocumentStatus.NotSubmitted, DocumentStatus.Submitted, DocumentStatus.XeroxCopy];

        /// <summary>
        /// Whether this paper is asked of the given student type in the given program. Unknown codes
        /// (a deleted requirement a historical checklist still references) stay visible.
        /// </summary>
        public bool AppliesTo(string code, StudentType studentType, ProgramTrack program) =>
            !_byCode.TryGetValue(code, out var e)
            || ((studentType == StudentType.Transferee ? e.AppliesToTransferees : e.AppliesToNewStudents)
                && e.Programs.Contains(program));
    }
}
