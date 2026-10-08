using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Paging;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.AcademicRecords;

namespace SENGENSystem.Server.Features.Enlistment.Approvals
{
    // Vertical slice: the Registrar's slot-approval queue (FR-ENL-04). Approval consumes a
    // seat under optimistic concurrency (Section.RowVersion) with the DB CHECK constraint as
    // the last-resort backstop, so racing approvals can never oversell the 40-slot cap
    // (FR-ENL-03); the decision is emailed to the student and audited.
    public record ApprovalRowDto(
        Guid RequestId,
        Guid SectionId,
        string StudentNumber,
        string StudentName,
        string Program,
        string SubjectCode,
        string SubjectTitle,
        string SectionCode,
        int Capacity,
        int Enrolled,
        string Status,
        string RequestedAtUtc,
        string? DecidedAtUtc,
        string? RejectionReason)
    {
        public static ApprovalRowDto From(SlotRequest r) =>
            new(
                r.Id,
                r.SectionId,
                r.StudentRegistration?.StudentNumber ?? string.Empty,
                r.StudentRegistration?.FullName ?? string.Empty,
                r.StudentRegistration?.Program.ToString() ?? string.Empty,
                r.Section?.Subject?.Code ?? string.Empty,
                r.Section?.Subject?.Title ?? string.Empty,
                r.Section?.SectionCode ?? string.Empty,
                r.Section?.Capacity ?? 0,
                r.Section?.EnrolledCount ?? 0,
                r.Status.ToString(),
                Utc(r.RequestedAtUtc)!,
                Utc(r.DecidedAtUtc),
                r.RejectionReason);

        private static string? Utc(DateTime? value) =>
            value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc).ToString("o") : null;
    }

    public record RejectRequest(string? Reason);

    /// <summary>
    /// FR-ENL-04 reversal: release an already-approved seat. Rejection only applies to a request
    /// still pending, so before this existed a mis-approval had no undo at all — the seat stayed
    /// consumed for the rest of the term.
    /// </summary>
    public record DropSeatRequest(string? Reason);

    // FR-ENL-03 manual override: raise a section's seat cap so a short section can be completed.
    public record OverrideCapacityRequest(int Capacity, string? Reason);

    /// <summary>
    /// FR-ENL-08 bulk decision. Either an explicit set of request ids (what the queue's checkboxes
    /// send) or, with <paramref name="AllPending"/>, every pending request in the active term —
    /// optionally narrowed to one student or one section, which is how the queue's "approve this
    /// student's whole load" and "fill this section" shortcuts are expressed.
    /// </summary>
    public record BulkApproveRequest(
        IReadOnlyList<Guid>? RequestIds,
        bool AllPending = false,
        Guid? StudentRegistrationId = null,
        Guid? SectionId = null);

    /// <summary>What happened to one request in a bulk run — approved, or skipped with the reason.</summary>
    public record BulkApprovalOutcomeDto(
        Guid RequestId, string StudentNumber, string SubjectCode, string SectionCode, bool Approved, string? Reason);

    public static class ApprovalsEndpoints
    {
        // Guard rail on the override so a typo can't create a 10,000-seat section.
        private const int MaxOverrideCapacity = 200;

        public static IEndpointRouteBuilder MapEnlistmentApprovals(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/enlistment/approvals")
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.Registrar), nameof(UserRole.SchoolAdmin)));

            group.MapGet("", ListAsync);
            group.MapPost("{requestId:guid}/approve", ApproveAsync);
            group.MapPost("{requestId:guid}/reject", RejectAsync);
            group.MapPost("{requestId:guid}/drop", DropAsync);
            group.MapPost("bulk-approve", BulkApproveAsync);

            // Overriding the cap is a broader authority than approving (FR-ENL-03) — the Academic
            // Head and School Admin may raise it too, so this route carries its own role policy
            // rather than inheriting the group's Registrar-only one.
            app.MapPost("/api/enlistment/approvals/sections/{sectionId:guid}/capacity", OverrideCapacityAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.Registrar), nameof(UserRole.AcademicHead), nameof(UserRole.SchoolAdmin)));
            return app;
        }

        private static async Task<IResult> ListAsync(
            string? status,
            string? search,
            int? page,
            int? pageSize,
            string? sort,
            string? dir,
            AppDbContext db,
            CancellationToken cancellationToken)
        {
            var query = db.SlotRequests.AsNoTracking()
                .Include(r => r.StudentRegistration)
                .Include(r => r.Section).ThenInclude(s => s!.Subject)
                .AsQueryable();

            // Scope to the active term's sections so the queue and its pending count stay correct
            // and compact after a semester rollover, rather than listing every past term's requests.
            if (await db.GetActiveSemesterIdAsync(cancellationToken) is { } activeSemesterId)
            {
                query = query.Where(r => r.Section!.SemesterId == activeSemesterId);
            }

            if (!string.IsNullOrWhiteSpace(status)
                && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<SlotRequestStatus>(status, ignoreCase: true, out var parsed))
            {
                query = query.Where(r => r.Status == parsed);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(r =>
                    r.StudentRegistration!.StudentNumber.Contains(term)
                    || r.StudentRegistration.LastName.Contains(term)
                    || r.StudentRegistration.FirstName.Contains(term)
                    || r.Section!.SectionCode.Contains(term));
            }

            var desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            var ordered = (sort?.ToLowerInvariant()) switch
            {
                "studentname" => desc
                    ? query.OrderByDescending(r => r.StudentRegistration!.LastName)
                        .ThenByDescending(r => r.StudentRegistration!.FirstName)
                    : query.OrderBy(r => r.StudentRegistration!.LastName)
                        .ThenBy(r => r.StudentRegistration!.FirstName),
                "subjectcode" => desc
                    ? query.OrderByDescending(r => r.Section!.Subject!.Code)
                    : query.OrderBy(r => r.Section!.Subject!.Code),
                "sectioncode" => desc
                    ? query.OrderByDescending(r => r.Section!.SectionCode)
                    : query.OrderBy(r => r.Section!.SectionCode),
                "seats" => desc
                    ? query.OrderByDescending(r => r.Section!.Capacity - r.Section!.EnrolledCount)
                    : query.OrderBy(r => r.Section!.Capacity - r.Section!.EnrolledCount),
                "status" => desc ? query.OrderByDescending(r => r.Status) : query.OrderBy(r => r.Status),
                "requestedatutc" => desc
                    ? query.OrderByDescending(r => r.RequestedAtUtc)
                    : query.OrderBy(r => r.RequestedAtUtc),
                // Pending first, newest within that — the queue's own order.
                _ => query.OrderBy(r => r.Status == SlotRequestStatus.Requested ? 0 : 1)
                        .ThenByDescending(r => r.RequestedAtUtc)
            };

            var result = await ordered.ThenBy(r => r.Id)
                .ToPagedAsync(PageSpec.From(page, pageSize), cancellationToken);

            // Counted in SQL over the whole filtered queue, not over the rows in this response.
            // Taken from the page it would have quietly become "pending on this page" the moment
            // paging arrived — and this is the number the Registrar reads to judge the backlog, and
            // the one the sidebar badge is checked against.
            var pendingCount = await query
                .CountAsync(r => r.Status == SlotRequestStatus.Requested, cancellationToken);

            var body = result.Select(ApprovalRowDto.From).ToResponse("requests");
            body["pendingCount"] = pendingCount;
            return Results.Ok(body);
        }

        private static async Task<IResult> ApproveAsync(
            Guid requestId,
            ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            Notifier notifier,
            Features.Reports.Live.ReportsBroadcaster broadcaster,
            EmailOutbox outbox,
            CancellationToken cancellationToken)
        {
            var request = await db.SlotRequests
                .Include(r => r.StudentRegistration)
                .Include(r => r.Section).ThenInclude(s => s!.Subject)
                .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

            if (request?.Section is null || request.StudentRegistration is null)
            {
                return Results.NotFound(new { message = "Request not found." });
            }

            var outcome = await TryApproveAsync(request, principal, db, audit, notifier, cancellationToken);
            if (!outcome.Approved)
            {
                return Results.Conflict(new { message = outcome.Reason });
            }

            broadcaster.Announce("enlistment");
            // Queued on the same path as the bulk leg, so a single approval and a batch of 500
            // deliver identically — one confirmation per approval, once.
            QueueApprovalEmail(request, outbox);
            audit.Record(AuditAction.NotificationDispatched,
                $"Queued slot-approval confirmation to {request.StudentRegistration.Email}.",
                "SlotRequest", request.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ApprovalRowDto.From(request));
        }

        /// <summary>
        /// FR-ENL-08: decide many requests in one action. The queue grows a hundred rows deep in the
        /// first days of enlistment, and clicking Approve a hundred times is the bottleneck the
        /// Registrar actually feels — but a bulk action must not become a blunt one. Every request
        /// still goes through the <i>same</i> per-request checks as a single approval (overlap,
        /// seat capacity, already-decided), and each one that cannot be approved is skipped with its
        /// own reason rather than failing the batch. The response reports every outcome, so a run
        /// that approved 47 of 50 says exactly which three didn't and why.
        /// </summary>
        private static async Task<IResult> BulkApproveAsync(
            BulkApproveRequest body,
            ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            Notifier notifier,
            Features.Reports.Live.ReportsBroadcaster broadcaster,
            EmailOutbox outbox,
            CancellationToken cancellationToken)
        {
            var query = db.SlotRequests
                .Include(r => r.StudentRegistration)
                .Include(r => r.Section).ThenInclude(s => s!.Subject)
                .Where(r => r.Status == SlotRequestStatus.Requested);

            if (body.RequestIds is { Count: > 0 })
            {
                var ids = body.RequestIds.ToList();
                query = query.Where(r => ids.Contains(r.Id));
            }
            else if (body.AllPending)
            {
                // Scope a sweep to the active term, the same way the queue itself is scoped, so
                // "approve all pending" can never reach back into a previous semester.
                if (await db.GetActiveSemesterIdAsync(cancellationToken) is { } activeSemesterId)
                {
                    query = query.Where(r => r.Section!.SemesterId == activeSemesterId);
                }
                if (body.StudentRegistrationId is { } studentId)
                {
                    query = query.Where(r => r.StudentRegistrationId == studentId);
                }
                if (body.SectionId is { } sectionId)
                {
                    query = query.Where(r => r.SectionId == sectionId);
                }
            }
            else
            {
                return Results.BadRequest(new
                {
                    message = "Choose the requests to approve, or set allPending to sweep the queue."
                });
            }

            var requests = await query
                // Oldest first: when a section runs out of seats mid-sweep, the students who
                // queued first are the ones who get them.
                .OrderBy(r => r.RequestedAtUtc)
                .Take(500)
                .ToListAsync(cancellationToken);

            if (requests.Count == 0)
            {
                return Results.Ok(new
                {
                    approvedCount = 0,
                    skippedCount = 0,
                    outcomes = Array.Empty<BulkApprovalOutcomeDto>()
                });
            }

            var outcomes = new List<BulkApprovalOutcomeDto>(requests.Count);
            var approved = new List<SlotRequest>();
            foreach (var request in requests)
            {
                if (request.Section is null || request.StudentRegistration is null)
                {
                    outcomes.Add(new BulkApprovalOutcomeDto(
                        request.Id, string.Empty, string.Empty, string.Empty, false,
                        "The section or student record no longer exists."));
                    continue;
                }

                var outcome = await TryApproveAsync(request, principal, db, audit, notifier, cancellationToken);
                outcomes.Add(new BulkApprovalOutcomeDto(
                    request.Id,
                    request.StudentRegistration.StudentNumber,
                    request.Section.Subject?.Code ?? string.Empty,
                    request.Section.SectionCode,
                    outcome.Approved,
                    outcome.Reason));
                if (outcome.Approved) approved.Add(request);
            }

            if (approved.Count > 0)
            {
                audit.Record(AuditAction.SlotApproved,
                    $"Bulk-approved {approved.Count} seat request(s)" +
                    (outcomes.Count > approved.Count
                        ? $"; {outcomes.Count - approved.Count} skipped."
                        : "."),
                    "SlotRequest", string.Empty);
                await db.SaveChangesAsync(cancellationToken);
                broadcaster.Announce("enlistment");
            }

            // Confirmations are queued, not sent. A 500-row sweep used to make 500 synchronous SMTP
            // calls before the response returned, which is the half of F-12 that actually hurt: the
            // Registrar's browser waited on a mail server for work that has nothing to do with the
            // decision they just made. The outbox commits these with the approvals and the
            // dispatcher delivers them afterwards.
            //
            // The *other* half of F-12 — "batch the saves" — is deliberately not done, and this is
            // the reasoning. The per-request SaveChangesAsync inside TryApproveAsync is not
            // incidental chattiness; it is the optimistic-concurrency retry that F-08 established
            // for the seat counter, and RevertApproval depends on each row committing independently
            // so one full section cannot poison the rest of the batch. Batching them would make a
            // single lost race fail the whole run, which trades a real integrity guarantee for
            // round trips — and would break the "approved 47 of 50, here is why three didn't"
            // contract this endpoint promises. The transport was the problem; the integrity was not.
            if (approved.Count > 0)
            {
                foreach (var request in approved)
                {
                    QueueApprovalEmail(request, outbox);
                }
                audit.Record(AuditAction.NotificationDispatched,
                    $"Queued {approved.Count} slot-approval confirmation(s).",
                    "SlotRequest", string.Empty);
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.Ok(new
            {
                approvedCount = approved.Count,
                skippedCount = outcomes.Count - approved.Count,
                outcomes
            });
        }

        /// <summary>
        /// Approve one request: the overlap re-check, the status change, the seat consumption under
        /// optimistic concurrency, the student's bell notice, and the section-full alert. Shared by
        /// the single and bulk endpoints so a bulk approval is never a weaker check than a single
        /// one. Returns the reason instead of throwing when the request cannot be approved; the
        /// caller sends the email.
        /// </summary>
        private static async Task<(bool Approved, string? Reason)> TryApproveAsync(
            SlotRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            Notifier notifier,
            CancellationToken cancellationToken)
        {
            if (request.Status != SlotRequestStatus.Requested)
            {
                return (false, $"This request is already {request.Status}.");
            }

            var section = request.Section!;

            // Re-check the prerequisite chain at decision time (FR-ENL-06 / F-10). The request leg
            // already refused an unmet prerequisite, so reaching here usually means the record
            // changed in between — a verdict corrected, or an evaluation reopened — and the seat
            // must not be granted on the strength of a check that is no longer true. Same helper,
            // same fall-open rule; only the wording differs, because this reader is staff and the
            // remedy is theirs.
            var history = await AcademicHistory.LoadAsync(db, request.StudentRegistrationId, cancellationToken);
            if (history.IsEnforceable)
            {
                var unmet = await history.UnmetPrerequisitesAsync(db, section.SubjectId, cancellationToken);
                if (unmet.Count > 0)
                {
                    var code = section.Subject?.Code ?? "this subject";
                    audit.Record(AuditAction.PrerequisiteBlocked,
                        $"Approval of {request.StudentRegistration!.StudentNumber}'s seat in {code} was " +
                        $"refused — unmet prerequisite(s): {string.Join(", ", unmet.Select(s => s.Code))}.",
                        "SlotRequest", request.Id.ToString());
                    return (false, AcademicHistory.Refusal(code, unmet, aboutSelf: false));
                }
            }

            // Re-check the overlap rule against the student's *approved* sections at decision
            // time (FR-ENL-07) — earlier approvals may have changed the picture.
            //
            // Scoped to the section's own term. Unscoped, a returning student's previous term's
            // approvals were compared against this term's candidate, and since those rows are still
            // published the comparison found a "clash" with a class that ended last semester — a
            // false conflict whose only offered remedy was to reject a perfectly valid request.
            // RequestSlot was corrected for the same reason (see its comment on the live-request
            // query); this leg was missed at the time.
            var approvedSectionIds = await db.SlotRequests.AsNoTracking()
                .Where(r => r.StudentRegistrationId == request.StudentRegistrationId
                    && r.Status == SlotRequestStatus.Approved
                    && r.Section!.SemesterId == section.SemesterId)
                .Select(r => r.SectionId)
                .ToListAsync(cancellationToken);
            if (approvedSectionIds.Count > 0)
            {
                var mySlots = await db.ScheduleAssignments.AsNoTracking()
                    .Where(a => approvedSectionIds.Contains(a.SectionId) && a.IsPublished)
                    .Include(a => a.TimeSlot)
                    .Select(a => a.TimeSlot!)
                    .ToListAsync(cancellationToken);
                var candidateSlots = await db.ScheduleAssignments.AsNoTracking()
                    .Where(a => a.SectionId == section.Id && a.IsPublished)
                    .Include(a => a.TimeSlot)
                    .Select(a => a.TimeSlot!)
                    .ToListAsync(cancellationToken);
                if (candidateSlots.Any(c => mySlots.Any(m => m.OverlapsWith(c))))
                {
                    return (false, "Approving this would give the student overlapping classes. Reject it instead.");
                }
            }

            request.Status = SlotRequestStatus.Approved;
            request.DecidedAtUtc = DateTime.UtcNow;
            request.DecidedByUserId = CurrentUserId(principal);
            audit.Record(AuditAction.SlotApproved,
                $"Approved {request.StudentRegistration!.StudentNumber}'s seat in " +
                $"{section.Subject?.Code} ({section.SectionCode}).",
                "SlotRequest", request.Id.ToString());
            // Bell notice (linked accounts only) commits with the approval; email follows later.
            if (request.StudentRegistration.UserId is { } approvedUserId)
            {
                notifier.Notify(approvedUserId, NotificationKind.EnlistmentApproved,
                    $"Seat approved: {section.Subject?.Code}",
                    $"Your seat in {section.Subject?.Code} ({section.SectionCode}) is confirmed. It will appear in My schedule once published.",
                    "/enlistment");
            }

            // Consume one seat under optimistic concurrency; the DB CHECK is the backstop.
            for (var attempt = 0; ; attempt++)
            {
                if (section.EnrolledCount >= section.Capacity)
                {
                    // Undo the in-memory decision so a skipped request isn't left looking approved.
                    RevertApproval(db, request);
                    return (false, $"Section {section.SectionCode} is full ({section.Capacity} seats). Reject the request instead.");
                }
                section.EnrolledCount++;
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    break;
                }
                catch (DbUpdateConcurrencyException) when (attempt < 5)
                {
                    // Another approval touched this section first — reload and re-check.
                    await db.Entry(section).ReloadAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    RevertApproval(db, request);
                    return (false, $"Section {section.SectionCode} is full ({section.Capacity} seats). Reject the request instead.");
                }
            }

            // If that approval just filled the section, alert the decision-makers (Registrar, Academic
            // Head, School Admin) so they can open another section, raise the cap (FR-ENL-03), or move
            // students (FR-NOTIF). Committed as its own small write; the approval already stands.
            if (section.EnrolledCount >= section.Capacity)
            {
                var deciderIds = await NotificationRecipients.DecisionMakerUserIdsAsync(db, cancellationToken);
                notifier.NotifyMany(deciderIds, NotificationKind.SectionFull,
                    $"Section full: {section.SectionCode}",
                    $"{section.Subject?.Code} ({section.SectionCode}) is now full at " +
                    $"{section.EnrolledCount}/{section.Capacity} seats. Open another section, raise the cap, " +
                    "or move students as needed.",
                    "/approvals");
                await db.SaveChangesAsync(cancellationToken);
            }

            return (true, null);
        }

        /// <summary>
        /// Roll a failed approval back to Requested. The seat was never committed, so only the
        /// in-memory request (and the bell notice queued alongside it) has to be undone before the
        /// next request in a bulk run is saved.
        /// </summary>
        private static void RevertApproval(AppDbContext db, SlotRequest request)
        {
            request.Status = SlotRequestStatus.Requested;
            request.DecidedAtUtc = null;
            request.DecidedByUserId = null;
            foreach (var entry in db.ChangeTracker.Entries<Notification>()
                .Where(e => e.State == EntityState.Added).ToList())
            {
                entry.State = EntityState.Detached;
            }
            foreach (var entry in db.ChangeTracker.Entries<AuditEntry>()
                .Where(e => e.State == EntityState.Added).ToList())
            {
                entry.State = EntityState.Detached;
            }
        }

        /// <summary>
        /// Stages one approval confirmation on the outbox (FR-ENL-04). Keyed on the request id, so a
        /// double-submitted approval cannot queue the same confirmation twice.
        /// </summary>
        private static void QueueApprovalEmail(SlotRequest request, EmailOutbox outbox)
        {
            var section = request.Section!;
            var (subject, body) = EnlistmentEmails.SlotApproved(
                request.StudentRegistration!,
                section.Subject?.Code ?? string.Empty,
                section.Subject?.Title ?? string.Empty,
                section.SectionCode);
            outbox.Queue(
                request.StudentRegistration!.Email, request.StudentRegistration.FullName,
                subject, body,
                kind: "EnlistmentApproval",
                dedupeKey: $"slot-approved:{request.Id}");
        }

        private static async Task<IResult> RejectAsync(
            Guid requestId,
            RejectRequest body,
            ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            Notifier notifier,
            Features.Reports.Live.ReportsBroadcaster broadcaster,
            IEmailSender email,
            CancellationToken cancellationToken)
        {
            var request = await db.SlotRequests
                .Include(r => r.StudentRegistration)
                .Include(r => r.Section).ThenInclude(s => s!.Subject)
                .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

            if (request?.Section is null || request.StudentRegistration is null)
            {
                return Results.NotFound(new { message = "Request not found." });
            }
            if (request.Status != SlotRequestStatus.Requested)
            {
                return Results.Conflict(new { message = $"This request is already {request.Status}." });
            }

            request.Status = SlotRequestStatus.Rejected;
            request.DecidedAtUtc = DateTime.UtcNow;
            request.DecidedByUserId = CurrentUserId(principal);
            request.RejectionReason = string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim();

            audit.Record(AuditAction.SlotRejected,
                $"Rejected {request.StudentRegistration.StudentNumber}'s seat request for " +
                $"{request.Section.Subject?.Code} ({request.Section.SectionCode}).",
                "SlotRequest", request.Id.ToString());
            if (request.StudentRegistration.UserId is { } rejectedUserId)
            {
                notifier.Notify(rejectedUserId, NotificationKind.EnlistmentRejected,
                    $"Seat request declined: {request.Section.Subject?.Code}",
                    request.RejectionReason is null
                        ? $"Your request for {request.Section.Subject?.Code} ({request.Section.SectionCode}) was declined. You can pick another section."
                        : $"Your request for {request.Section.Subject?.Code} ({request.Section.SectionCode}) was declined: {request.RejectionReason}",
                    "/enlistment");
            }
            await db.SaveChangesAsync(cancellationToken);
            broadcaster.Announce("enlistment");

            var (subject, bodyHtml) = EnlistmentEmails.SlotRejected(
                request.StudentRegistration,
                request.Section.Subject?.Code ?? string.Empty,
                request.Section.Subject?.Title ?? string.Empty,
                request.Section.SectionCode,
                request.RejectionReason);
            var sent = await email.SendAsync(
                request.StudentRegistration.Email, request.StudentRegistration.FullName,
                subject, bodyHtml, cancellationToken);
            if (sent.Sent)
            {
                audit.Record(AuditAction.NotificationDispatched,
                    $"Sent slot-rejection notice to {request.StudentRegistration.Email}.",
                    "SlotRequest", request.Id.ToString());
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.Ok(ApprovalRowDto.From(request));
        }

        /// <summary>
        /// POST {requestId}/drop — release an approved seat (FR-ENL-04). Rejection covers a request
        /// that is still pending; this covers the one already granted, which previously had no undo:
        /// a student approved into the wrong section stayed in it, and the seat stayed spent.
        /// <para>
        /// The seat itself is returned by <see cref="SeatRelease"/>, the single place allowed to
        /// decrement the counter, so a staff drop and a student's own drop cannot diverge.
        /// </para>
        /// </summary>
        private static async Task<IResult> DropAsync(
            Guid requestId,
            DropSeatRequest body,
            ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            Notifier notifier,
            Features.Reports.Live.ReportsBroadcaster broadcaster,
            CancellationToken cancellationToken)
        {
            var request = await db.SlotRequests
                .Include(r => r.StudentRegistration)
                .Include(r => r.Section).ThenInclude(s => s!.Subject)
                .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

            if (request?.Section is null || request.StudentRegistration is null)
            {
                return Results.NotFound(new { message = "Request not found." });
            }

            var outcome = await SeatRelease.ReleaseAsync(
                request, CurrentUserId(principal), body.Reason,
                byStudent: false, db, audit, notifier, cancellationToken);
            if (!outcome.Dropped)
            {
                return Results.Conflict(new { message = outcome.Reason });
            }

            broadcaster.Announce("enlistment");
            return Results.Ok(ApprovalRowDto.From(request));
        }

        // POST /api/enlistment/approvals/sections/{sectionId}/capacity — raise a section's seat cap
        // so a section short of a full block can be completed (FR-ENL-03). The new cap can never be
        // set below the seats already approved, and is bounded so a typo can't create a huge section.
        private static async Task<IResult> OverrideCapacityAsync(
            Guid sectionId,
            OverrideCapacityRequest body,
            ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            Features.Reports.Live.ReportsBroadcaster broadcaster,
            CancellationToken cancellationToken)
        {
            var section = await db.Sections
                .Include(s => s.Subject)
                .FirstOrDefaultAsync(s => s.Id == sectionId, cancellationToken);
            if (section is null)
            {
                return Results.NotFound(new { message = "Section not found." });
            }

            if (body.Capacity < 1 || body.Capacity > MaxOverrideCapacity)
            {
                return Results.BadRequest(new
                {
                    message = $"Capacity must be between 1 and {MaxOverrideCapacity} seats."
                });
            }
            if (body.Capacity < section.EnrolledCount)
            {
                return Results.Conflict(new
                {
                    message = $"{section.SectionCode} already has {section.EnrolledCount} approved seats — " +
                              $"the cap can't be set below that."
                });
            }
            if (body.Capacity == section.Capacity)
            {
                return Results.Ok(new
                {
                    sectionId = section.Id,
                    sectionCode = section.SectionCode,
                    capacity = section.Capacity,
                    enrolled = section.EnrolledCount,
                    previousCapacity = section.Capacity
                });
            }

            var previous = section.Capacity;
            var reason = string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim();
            section.Capacity = body.Capacity;

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    audit.Record(AuditAction.SectionCapacityOverridden,
                        $"Raised the seat cap of {section.Subject?.Code} ({section.SectionCode}) from {previous} to {section.Capacity}" +
                        (reason is null ? "." : $" — {reason}."),
                        "Section", section.Id.ToString());
                    await db.SaveChangesAsync(cancellationToken);
                    break;
                }
                catch (DbUpdateConcurrencyException) when (attempt < 5)
                {
                    // A racing approval consumed a seat first — reload, re-check the floor, re-apply.
                    await db.Entry(section).ReloadAsync(cancellationToken);
                    if (body.Capacity < section.EnrolledCount)
                    {
                        return Results.Conflict(new
                        {
                            message = $"{section.SectionCode} now has {section.EnrolledCount} approved seats — " +
                                      $"the cap can't be set below that."
                        });
                    }
                    section.Capacity = body.Capacity;
                }
            }

            broadcaster.Announce("enlistment");
            return Results.Ok(new
            {
                sectionId = section.Id,
                sectionCode = section.SectionCode,
                capacity = section.Capacity,
                enrolled = section.EnrolledCount,
                previousCapacity = previous
            });
        }

        private static Guid? CurrentUserId(ClaimsPrincipal principal) =>
            Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    }
}
