using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

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

    // FR-ENL-03 manual override: raise a section's seat cap so a short section can be completed.
    public record OverrideCapacityRequest(int Capacity, string? Reason);

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

            var items = await query
                .OrderBy(r => r.Status == SlotRequestStatus.Requested ? 0 : 1)
                .ThenByDescending(r => r.RequestedAtUtc)
                .Take(500)
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                count = items.Count,
                pendingCount = items.Count(r => r.Status == SlotRequestStatus.Requested),
                requests = items.Select(ApprovalRowDto.From).ToList()
            });
        }

        private static async Task<IResult> ApproveAsync(
            Guid requestId,
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

            var section = request.Section;

            // Re-check the overlap rule against the student's *approved* sections at decision
            // time (FR-ENL-07) — earlier approvals may have changed the picture.
            var approvedSectionIds = await db.SlotRequests.AsNoTracking()
                .Where(r => r.StudentRegistrationId == request.StudentRegistrationId
                    && r.Status == SlotRequestStatus.Approved)
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
                    return Results.Conflict(new
                    {
                        message = "Approving this would give the student overlapping classes. Reject it instead."
                    });
                }
            }

            request.Status = SlotRequestStatus.Approved;
            request.DecidedAtUtc = DateTime.UtcNow;
            request.DecidedByUserId = CurrentUserId(principal);
            audit.Record(AuditAction.SlotApproved,
                $"Approved {request.StudentRegistration.StudentNumber}'s seat in " +
                $"{section.Subject?.Code} ({section.SectionCode}).",
                "SlotRequest", request.Id.ToString());
            // Bell notice (linked accounts only) commits with the approval; email follows below.
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
                    return Results.Conflict(new
                    {
                        message = $"Section {section.SectionCode} is full ({section.Capacity} seats). Reject the request instead."
                    });
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
                    return Results.Conflict(new
                    {
                        message = $"Section {section.SectionCode} is full ({section.Capacity} seats). Reject the request instead."
                    });
                }
            }

            broadcaster.Announce("enlistment");

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

            // Decision email is best-effort: the approval is already committed (FR-ENL-04).
            var (subject, body) = EnlistmentEmails.SlotApproved(
                request.StudentRegistration,
                section.Subject?.Code ?? string.Empty,
                section.Subject?.Title ?? string.Empty,
                section.SectionCode);
            var sent = await email.SendAsync(
                request.StudentRegistration.Email, request.StudentRegistration.FullName,
                subject, body, cancellationToken);
            if (sent.Sent)
            {
                audit.Record(AuditAction.NotificationDispatched,
                    $"Sent slot-approval confirmation to {request.StudentRegistration.Email}.",
                    "SlotRequest", request.Id.ToString());
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.Ok(ApprovalRowDto.From(request));
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
