using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Paging;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Documents.Checklist
{
    // Vertical slice: the Admission Officer's per-enrollee requirements checklist board
    // (FR-DOC-01..03). Lists every enrollee with per-document states and completion, and
    // records/updates each paper's submission status as an auditable action.
    /// <summary>
    /// One checklist line. <paramref name="Statuses"/> is the set the officer may record against
    /// this paper — the catalog decides whether a photocopy or a certificate of grades is the
    /// third option, so the client never has to know which requirement is the transcript.
    /// </summary>
    public record ChecklistDocumentDto(
        Guid Id,
        string RequirementCode,
        string Label,
        string Status,
        bool GatesAuthorization,
        IReadOnlyList<string> Statuses,
        // Who last decided this row and when (FR-DOC-03). Both null for a paper nobody has ruled on
        // yet, which is the honest reading of a freshly seeded checklist rather than a missing value.
        string? UpdatedAtUtc,
        string? VerifiedBy,
        // The day the paper was received ("yyyy-MM-dd"); null while it is not submitted, or for a
        // paper received before dates were kept.
        string? SubmittedOn)
    {
        public static ChecklistDocumentDto From(
            RegistrationDocument d, RequirementCatalog catalog, IReadOnlyDictionary<Guid, string> verifiers) =>
            new(d.Id,
                d.RequirementCode,
                catalog.Label(d.RequirementCode),
                d.Status.ToString(),
                catalog.GatesAuthorization(d.RequirementCode),
                catalog.StatusesFor(d.RequirementCode).Select(s => s.ToString()).ToList(),
                d.UpdatedAtUtc is { } at
                    ? DateTime.SpecifyKind(at, DateTimeKind.Utc).ToString("o")
                    : null,
                d.VerifiedByUserId is { } by ? verifiers.GetValueOrDefault(by) : null,
                d.SubmittedOn?.ToString("yyyy-MM-dd"));
    }

    public record ChecklistRowDto(
        Guid RegistrationId,
        // SEN-GEN's own registration number ("2026-000001")…
        string StudentNumber,
        // …and the official Student ID from the student-records system, once the Admission Officer
        // has recorded it (or a checklist import has learnt it). Null until then.
        string? OfficialStudentNumber,
        string FullName,
        string Program,
        string StudentType,
        string RegistrationStatus,
        string? SemesterName,
        bool IsPreAuthorized,
        bool IsComplete,
        int SubmittedCount,
        int TotalCount,
        // The most recent day any of this enrollee's papers was received ("yyyy-MM-dd"), so the
        // board can show — and sort by — who has been handing things in.
        string? LastSubmittedOn,
        IReadOnlyList<string> MissingAuthorizationRequirements,
        IReadOnlyList<ChecklistDocumentDto> Documents)
    {
        public static ChecklistRowDto From(
            StudentRegistration r, RequirementCatalog catalog, IReadOnlyDictionary<Guid, string> verifiers)
        {
            var documents = DocumentChecklist.Applicable(r, catalog);
            return new(
                r.Id,
                r.StudentNumber,
                r.OfficialStudentNumber,
                r.FullName,
                r.Program.ToString(),
                r.StudentType.ToString(),
                r.Status.ToString(),
                r.Semester?.Name,
                r.IsPreAuthorized,
                DocumentChecklist.IsComplete(documents),
                DocumentChecklist.SubmittedCount(documents),
                documents.Count,
                documents.Where(d => d.Status != DocumentStatus.NotSubmitted).Max(d => d.SubmittedOn)?.ToString("yyyy-MM-dd"),
                DocumentChecklist.MissingAuthorizationRequirements(documents, catalog),
                documents
                    .OrderBy(d => catalog.Order(d.RequirementCode))
                    .Select(d => ChecklistDocumentDto.From(d, catalog, verifiers))
                    .ToList());
        }
    }

    /// <summary>
    /// A decision on one paper. <paramref name="Status"/> may be omitted to change only the date;
    /// <paramref name="SubmittedOn"/> ("yyyy-MM-dd") defaults to today when a paper is first marked received.
    /// </summary>
    public record UpdateDocumentStatusRequest(string? Status, string? SubmittedOn = null);

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
            int? page,
            int? pageSize,
            string? sort,
            string? dir,
            AppDbContext db,
            CancellationToken cancellationToken)
        {
            // The board before the completion chip narrows it. The headline tally is counted against
            // this, so switching the view to "Incomplete" cannot make the board report that nobody
            // is complete.
            var baseQuery = (await BoardQueryAsync(db, null, search, cancellationToken))
                .Include(r => r.Semester)
                .Include(r => r.Documents);
            var query = ApplyCompletion(baseQuery, completion);

            // Which papers gate clearance for enlistment — needed by the "Clearance" sort below.
            // Fetched once (a handful of rows) so the ordering itself can still be done in SQL.
            var gatingCodes = await db.AdmissionRequirements.AsNoTracking()
                .Where(a => a.IsActive && a.IsRequiredForAuthorization)
                .Select(a => a.Code)
                .ToListAsync(cancellationToken);

            var desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            var ordered = (sort?.ToLowerInvariant()) switch
            {
                // Enrollees still holding back their own clearance sort to the top ascending —
                // the same rule the column displays.
                "clearance" => desc
                    ? query.OrderByDescending(r => !r.Documents.Any(d =>
                        gatingCodes.Contains(d.RequirementCode) && d.Status == DocumentStatus.NotSubmitted))
                    : query.OrderBy(r => !r.Documents.Any(d =>
                        gatingCodes.Contains(d.RequirementCode) && d.Status == DocumentStatus.NotSubmitted)),
                "studentnumber" => desc
                    ? query.OrderByDescending(r => r.StudentNumber) : query.OrderBy(r => r.StudentNumber),
                // Students with no Student ID yet sort together (last ascending) rather than first.
                "officialstudentnumber" => desc
                    ? query.OrderBy(r => r.OfficialStudentNumber == null).ThenByDescending(r => r.OfficialStudentNumber)
                    : query.OrderBy(r => r.OfficialStudentNumber == null).ThenBy(r => r.OfficialStudentNumber),
                "fullname" => desc
                    ? query.OrderByDescending(r => r.LastName).ThenByDescending(r => r.FirstName)
                    : query.OrderBy(r => r.LastName).ThenBy(r => r.FirstName),
                "program" => desc ? query.OrderByDescending(r => r.Program) : query.OrderBy(r => r.Program),
                "studenttype" => desc
                    ? query.OrderByDescending(r => r.StudentType) : query.OrderBy(r => r.StudentType),
                "submittedcount" => desc
                    ? query.OrderByDescending(r => r.Documents.Count(d => d.Status != DocumentStatus.NotSubmitted))
                    : query.OrderBy(r => r.Documents.Count(d => d.Status != DocumentStatus.NotSubmitted)),
                // Most recently received first when descending; checklists with nothing dated sort last either way.
                "lastsubmittedon" => desc
                    ? query.OrderByDescending(r => r.Documents.Max(d => d.SubmittedOn))
                    : query.OrderBy(r => r.Documents.Max(d => d.SubmittedOn) == null)
                        .ThenBy(r => r.Documents.Max(d => d.SubmittedOn)),
                "registrationstatus" => desc
                    ? query.OrderByDescending(r => r.Status) : query.OrderBy(r => r.Status),
                "iscomplete" => desc
                    ? query.OrderByDescending(r => r.Documents.Any(d => d.Status == DocumentStatus.NotSubmitted))
                    : query.OrderBy(r => r.Documents.Any(d => d.Status == DocumentStatus.NotSubmitted)),
                _ => query.OrderByDescending(r => r.CreatedAtUtc)
            };

            var paged = await ordered.ThenBy(r => r.Id)
                .ToPagedAsync(PageSpec.From(page, pageSize), cancellationToken);

            var catalog = await DocumentChecklist.LoadCatalogAsync(db, cancellationToken);

            // Names for whoever decided the rows on this page. Resolved from the ids actually
            // present rather than by joining every user — a term's checklists are worked by a
            // handful of officers, so this is a short lookup however long the board gets.
            var verifierIds = paged.Items
                .SelectMany(r => r.Documents)
                .Select(d => d.VerifiedByUserId)
                .OfType<Guid>()
                .Distinct()
                .ToList();
            var verifiers = await VerifierNamesAsync(db, verifierIds, cancellationToken);

            var list = paged.Items.Select(r => ChecklistRowDto.From(r, catalog, verifiers)).ToList();

            // Counted in SQL across the whole board — not this page, and not the current view.
            var completeCount = await baseQuery
                .CountAsync(r => !r.Documents.Any(d => d.Status == DocumentStatus.NotSubmitted), cancellationToken);

            // How many enrollees a "Send reminders" sweep would actually email — every non-rejected
            // registration in the current term with at least one unsubmitted paper, independent of
            // the current filter or search — so the confirmation can state a true count (mirrors
            // SendRemindersEndpoint, which is scoped the same way: a reminder blast must never reach
            // a student whose term is already over).
            var reminderSemesterId = await db.GetActiveSemesterIdAsync(cancellationToken);
            var reminderTargetCount = await db.StudentRegistrations.AsNoTracking()
                .CountAsync(r => r.Status != RegistrationStatus.Rejected
                    && (reminderSemesterId == null || r.SemesterId == reminderSemesterId)
                    && r.Documents.Any(d => d.Status == DocumentStatus.NotSubmitted), cancellationToken);

            var body = new Paged<ChecklistRowDto>(list, paged.Total, paged.Page, paged.PageSize)
                .ToResponse("checklists");
            body["completeCount"] = completeCount;
            body["reminderTargetCount"] = reminderTargetCount;
            return Results.Ok(body);
        }

        /// <summary>
        /// The board's rows before paging and sorting — shared with the spreadsheet export so the
        /// file holds exactly the enrollees the officer is looking at. Scoped to the current term
        /// so the board doesn't carry every past term's enrollees forward when the semester rolls
        /// over; a search deliberately widens to every term so a past student can still be looked
        /// up (same rule as the registration queue). Search covers both identifiers and the name.
        /// </summary>
        internal static async Task<IQueryable<StudentRegistration>> BoardQueryAsync(
            AppDbContext db, string? completion, string? search, CancellationToken cancellationToken)
        {
            var query = db.StudentRegistrations.AsNoTracking().AsQueryable();

            if (string.IsNullOrWhiteSpace(search)
                && await db.GetActiveSemesterIdAsync(cancellationToken) is { } activeSemesterId)
            {
                query = query.Where(r => r.SemesterId == activeSemesterId);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(r =>
                    r.StudentNumber.Contains(term)
                    || (r.OfficialStudentNumber != null && r.OfficialStudentNumber.Contains(term))
                    || r.LastName.Contains(term)
                    || r.FirstName.Contains(term));
            }

            return ApplyCompletion(query, completion);
        }

        // The completion filter has to happen in SQL now that the list is paged. It used to run
        // in memory over the fetched rows, which paging would have turned into "filter this
        // page" — page 1 of an incomplete-only view would show whichever of the first 25
        // enrollees happened to be incomplete, and the total would count both kinds.
        //
        // "Complete" here is "no paper still unsubmitted". The displayed figure counts only the
        // papers that apply to the enrollee's program and student type (DocumentChecklist.Applicable,
        // which needs the catalog and cannot run in SQL); those agree for every checklist seeded
        // since applicability existed, and the AddRequirementApplicabilityAndClassStart migration
        // deleted the inapplicable historical rows precisely so they would.
        private static IQueryable<StudentRegistration> ApplyCompletion(IQueryable<StudentRegistration> query, string? completion)
        {
            if (string.Equals(completion, "complete", StringComparison.OrdinalIgnoreCase))
            {
                return query.Where(r => !r.Documents.Any(d => d.Status == DocumentStatus.NotSubmitted));
            }
            if (string.Equals(completion, "incomplete", StringComparison.OrdinalIgnoreCase))
            {
                return query.Where(r => r.Documents.Any(d => d.Status == DocumentStatus.NotSubmitted));
            }
            return query;
        }

        private static async Task<IResult> UpdateStatusAsync(
            Guid documentId,
            UpdateDocumentStatusRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            CancellationToken cancellationToken)
        {
            DateOnly? submittedOn = null;
            if (!string.IsNullOrWhiteSpace(request.SubmittedOn))
            {
                if (!DateOnly.TryParseExact(request.SubmittedOn, "yyyy-MM-dd", out var parsedDate))
                {
                    return Results.BadRequest(new { message = "Enter the date received as a valid date." });
                }
                if (DocumentChecklist.SubmittedOnProblem(parsedDate) is { } dateProblem)
                {
                    return Results.BadRequest(new { message = dateProblem });
                }
                submittedOn = parsedDate;
            }

            DocumentStatus? requested = null;
            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                if (!Enum.TryParse<DocumentStatus>(request.Status, ignoreCase: true, out var parsedStatus)
                    || !Enum.IsDefined(parsedStatus))
                {
                    return Results.BadRequest(new { message = "Please choose a valid document status." });
                }
                requested = parsedStatus;
            }
            else if (submittedOn is null)
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
            var catalog = await DocumentChecklist.LoadCatalogAsync(db, cancellationToken);
            var status = requested ?? document.Status;
            if (status == DocumentStatus.NotSubmitted && submittedOn is not null)
            {
                return Results.BadRequest(new { message = "A paper that has not been submitted has no date received." });
            }

            // Which third option this paper offers is the catalog's decision (a photocopy, or a
            // certificate of grades standing in for it) — never both. Refuse the one that isn't on
            // offer so a stale board or a hand-made request can't record an impossible state.
            var allowed = catalog.StatusesFor(document.RequirementCode);
            if (!allowed.Contains(status))
            {
                return Results.BadRequest(new
                {
                    message = $"{catalog.Label(document.RequirementCode)} cannot be recorded as " +
                        $"{Label(status)} — it accepts {string.Join(", ", allowed.Select(Label))}."
                });
            }

            var previous = document.Status;
            var previousDate = document.SubmittedOn;
            var officerId = Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedOfficer)
                && parsedOfficer != Guid.Empty ? parsedOfficer : (Guid?)null;
            // FR-DOC-03: the row carries its own provenance, and the date the paper came in, set
            // together through the one helper every path uses.
            if (DocumentChecklist.Record(document, status, submittedOn, officerId, DateTime.UtcNow))
            {
                var what = previous != status
                    ? $"from {previous} to {status}"
                    : $"date received from {previousDate?.ToString("MMM d, yyyy") ?? "unknown"}";
                audit.Record(AuditAction.DocumentChecklistUpdated,
                    $"Set {catalog.Label(document.RequirementCode)} of {registration.StudentNumber} {what}" +
                    (document.SubmittedOn is { } on ? $" (received {on:MMM d, yyyy})." : "."),
                    "StudentRegistration", registration.Id.ToString());
                await db.SaveChangesAsync(cancellationToken);
            }

            var applicable = DocumentChecklist.Applicable(registration, catalog);
            return Results.Ok(new
            {
                documentId = document.Id,
                status = document.Status.ToString(),
                submittedOn = document.SubmittedOn?.ToString("yyyy-MM-dd"),
                updatedAtUtc = Iso(document.UpdatedAtUtc),
                verifiedBy = await VerifierNameAsync(db, document.VerifiedByUserId, cancellationToken),
                isComplete = DocumentChecklist.IsComplete(applicable),
                submittedCount = DocumentChecklist.SubmittedCount(applicable),
                totalCount = applicable.Count,
                missingAuthorizationRequirements =
                    DocumentChecklist.MissingAuthorizationRequirements(applicable, catalog)
            });
        }

        /// <summary>
        /// Display names for the staff who decided a set of checklist rows. A user who has since
        /// been deleted simply drops out of the map, and the row shows a timestamp with no name
        /// rather than failing — the decision still happened.
        /// </summary>
        private static async Task<Dictionary<Guid, string>> VerifierNamesAsync(
            AppDbContext db, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
        {
            if (userIds.Count == 0) return [];
            return await db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FirstName + " " + u.LastName, cancellationToken);
        }

        private static async Task<string?> VerifierNameAsync(
            AppDbContext db, Guid? userId, CancellationToken cancellationToken)
        {
            if (userId is not { } id) return null;
            var names = await VerifierNamesAsync(db, [id], cancellationToken);
            return names.GetValueOrDefault(id);
        }

        private static string? Iso(DateTime? value) =>
            value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc).ToString("o") : null;

        /// <summary>Reader-friendly name for a status, for messages the officer sees.</summary>
        private static string Label(DocumentStatus status) => status switch
        {
            DocumentStatus.NotSubmitted => "not submitted",
            DocumentStatus.Submitted => "submitted (original)",
            DocumentStatus.XeroxCopy => "xerox copy",
            DocumentStatus.CertificateOfGrades => "certificate of grades",
            _ => status.ToString()
        };
    }
}
