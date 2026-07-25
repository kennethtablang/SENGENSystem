using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Documents.Checklist
{
    // Vertical slice: the Admission Officer's per-enrollee requirements checklist board
    // (FR-DOC-01..03). Lists every enrollee with per-document states and completion, and
    // records/updates each paper's submission status as an auditable action.
    public record ChecklistDocumentDto(Guid Id, string RequirementCode, string Label, string Status)
    {
        public static ChecklistDocumentDto From(RegistrationDocument d, RequirementCatalog catalog) =>
            new(d.Id, d.RequirementCode, catalog.Label(d.RequirementCode), d.Status.ToString());
    }

    public record ChecklistRowDto(
        Guid RegistrationId,
        string StudentNumber,
        string FullName,
        string Program,
        string StudentType,
        string RegistrationStatus,
        string? SemesterName,
        bool IsPreAuthorized,
        bool IsComplete,
        int SubmittedCount,
        int TotalCount,
        IReadOnlyList<ChecklistDocumentDto> Documents)
    {
        public static ChecklistRowDto From(StudentRegistration r, RequirementCatalog catalog) =>
            new(
                r.Id,
                r.StudentNumber,
                r.FullName,
                r.Program.ToString(),
                r.StudentType.ToString(),
                r.Status.ToString(),
                r.Semester?.Name,
                r.IsPreAuthorized,
                DocumentChecklist.IsComplete(r.Documents),
                DocumentChecklist.SubmittedCount(r.Documents),
                r.Documents.Count,
                r.Documents
                    .OrderBy(d => catalog.Order(d.RequirementCode))
                    .Select(d => ChecklistDocumentDto.From(d, catalog))
                    .ToList());
    }

    public record UpdateDocumentStatusRequest(string? Status);

    public static class DocumentChecklistEndpoints
    {
        public static IEndpointRouteBuilder MapDocumentChecklist(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/documents")
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.AdmissionOfficer), nameof(UserRole.Registrar), nameof(UserRole.SchoolAdmin)));

            group.MapGet("", ListAsync);
            group.MapPut("{documentId:guid}", UpdateStatusAsync);
            return app;
        }

        private static async Task<IResult> ListAsync(
            string? completion,
            string? search,
            AppDbContext db,
            CancellationToken cancellationToken)
        {
            var query = db.StudentRegistrations
                .AsNoTracking()
                .Include(r => r.Semester)
                .Include(r => r.Documents)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(r =>
                    r.StudentNumber.Contains(term)
                    || r.LastName.Contains(term)
                    || r.FirstName.Contains(term));
            }

            var items = await query
                .OrderByDescending(r => r.CreatedAtUtc)
                .Take(500)
                .ToListAsync(cancellationToken);

            var catalog = await DocumentChecklist.LoadCatalogAsync(db, cancellationToken);

            // Completion is derived from the document rows, so filter in memory.
            var rows = items.Select(r => ChecklistRowDto.From(r, catalog));
            if (string.Equals(completion, "complete", StringComparison.OrdinalIgnoreCase))
            {
                rows = rows.Where(r => r.IsComplete);
            }
            else if (string.Equals(completion, "incomplete", StringComparison.OrdinalIgnoreCase))
            {
                rows = rows.Where(r => !r.IsComplete);
            }

            var list = rows.ToList();

            // How many enrollees a "Send reminders" sweep would actually email — every non-rejected
            // registration with at least one unsubmitted paper, independent of the current filter or
            // search — so the confirmation can state a true count (mirrors SendRemindersEndpoint).
            var reminderTargetCount = await db.StudentRegistrations.AsNoTracking()
                .CountAsync(r => r.Status != RegistrationStatus.Rejected
                    && r.Documents.Any(d => d.Status == DocumentStatus.NotSubmitted), cancellationToken);

            return Results.Ok(new
            {
                count = list.Count,
                completeCount = list.Count(r => r.IsComplete),
                reminderTargetCount,
                checklists = list
            });
        }

        private static async Task<IResult> UpdateStatusAsync(
            Guid documentId,
            UpdateDocumentStatusRequest request,
            AppDbContext db,
            AuditLog audit,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Status)
                || !Enum.TryParse<DocumentStatus>(request.Status, ignoreCase: true, out var status)
                || !Enum.IsDefined(status))
            {
                return Results.BadRequest(new { message = "Please choose a valid document status." });
            }

            var document = await db.RegistrationDocuments
                .Include(d => d.StudentRegistration).ThenInclude(r => r!.Documents)
                .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

            if (document?.StudentRegistration is null)
            {
                return Results.NotFound(new { message = "Document record not found." });
            }

            var registration = document.StudentRegistration;
            if (document.Status != status)
            {
                var previous = document.Status;
                document.Status = status;
                var catalog = await DocumentChecklist.LoadCatalogAsync(db, cancellationToken);
                audit.Record(AuditAction.DocumentChecklistUpdated,
                    $"Set {catalog.Label(document.RequirementCode)} of {registration.StudentNumber} " +
                    $"from {previous} to {status}.",
                    "StudentRegistration", registration.Id.ToString());
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.Ok(new
            {
                documentId = document.Id,
                status = document.Status.ToString(),
                isComplete = DocumentChecklist.IsComplete(registration.Documents),
                submittedCount = DocumentChecklist.SubmittedCount(registration.Documents),
                totalCount = registration.Documents.Count
            });
        }
    }
}
