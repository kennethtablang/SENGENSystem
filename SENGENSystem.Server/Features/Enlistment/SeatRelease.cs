using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Enlistment
{
    /// <summary>
    /// Gives an approved seat back (FR-ENL-04). The mirror image of the approval in
    /// <c>ApprovalsEndpoints.TryApproveAsync</c>, and deliberately the <b>only</b> place in the
    /// system that decrements <see cref="Section.EnrolledCount"/>.
    /// <para>
    /// Until this existed the counter only ever went up: a mis-approval could not be undone, a
    /// student approved into the wrong section was stuck in it for the term, and a withdrawal left
    /// its seats consumed forever. Every figure derived from the counter — dashboard fill %, the
    /// "No. of students" column on the faculty loading report, the section-full alert, the
    /// capacity-override decision — inherited that drift, so the fix belongs here rather than in
    /// each reader.
    /// </para>
    /// <para>
    /// Concurrency mirrors the approval exactly: the decrement rides <see cref="Section.RowVersion"/>
    /// and retries on a lost race, and the <c>CK_Sections_EnrolledCount</c> CHECK (<c>&gt;= 0</c>)
    /// is the backstop, so a drop racing an approval for the same section cannot corrupt the count
    /// in either direction.
    /// </para>
    /// </summary>
    internal static class SeatRelease
    {
        internal sealed record DropOutcome(bool Dropped, string? Reason);

        /// <summary>
        /// Move an approved request to <see cref="SlotRequestStatus.Dropped"/> and return its seat.
        /// The caller supplies <paramref name="actorUserId"/> (the student or the staff member) and
        /// the audit wording, because a self-drop and a staff correction are the same mechanism but
        /// not the same event.
        /// </summary>
        /// <param name="request">
        /// Must be tracked, with <c>Section</c> and <c>Section.Subject</c> loaded — the section is
        /// mutated and the messages name the subject.
        /// </param>
        public static async Task<DropOutcome> ReleaseAsync(
            SlotRequest request,
            Guid? actorUserId,
            string? reason,
            bool byStudent,
            AppDbContext db,
            AuditLog audit,
            Notifier notifier,
            CancellationToken cancellationToken)
        {
            if (request.Status != SlotRequestStatus.Approved)
            {
                // A pending request is cancelled, not dropped — it never took a seat, so routing it
                // through here would decrement a count it never incremented.
                return new DropOutcome(false,
                    $"Only an approved seat can be dropped — this request is {request.Status}.");
            }

            var section = request.Section
                ?? throw new InvalidOperationException("SeatRelease requires the section to be loaded.");

            var subjectCode = section.Subject?.Code ?? string.Empty;
            var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

            request.Status = SlotRequestStatus.Dropped;
            request.DroppedAtUtc = DateTime.UtcNow;
            request.DroppedByUserId = actorUserId;
            request.DropReason = trimmedReason;

            audit.Record(AuditAction.SlotDropped,
                byStudent
                    ? $"{request.StudentRegistration?.StudentNumber} dropped their seat in " +
                      $"{subjectCode} ({section.SectionCode})." + Suffix(trimmedReason)
                    : $"Dropped {request.StudentRegistration?.StudentNumber}'s seat in " +
                      $"{subjectCode} ({section.SectionCode})." + Suffix(trimmedReason),
                "SlotRequest", request.Id.ToString());

            // A staff drop is news to the student; a self-drop is not — they just did it. Committed
            // in the same transaction as the drop, the way the approval notice is.
            if (!byStudent && request.StudentRegistration?.UserId is { } studentUserId)
            {
                notifier.Notify(studentUserId, NotificationKind.EnlistmentRejected,
                    $"Seat released: {subjectCode}",
                    $"Your seat in {subjectCode} ({section.SectionCode}) has been released by the " +
                    "Registrar." + Suffix(trimmedReason) +
                    " The class will drop off My schedule; request another section if you still need the subject.",
                    "/enlistment");
            }

            // Return the seat under the same optimistic concurrency the approval takes it with.
            for (var attempt = 0; ; attempt++)
            {
                // Never below zero. If the counter has already drifted (rows approved before this
                // release path existed), refuse rather than push it negative and trip the CHECK —
                // and say so, because a mismatch is a real finding the staff should see.
                if (section.EnrolledCount <= 0)
                {
                    RevertDrop(db, request);
                    return new DropOutcome(false,
                        $"{section.SectionCode} already reads 0 enrolled, so there is no seat to give " +
                        "back. The section's enrolled count and its approved seats disagree — report this.");
                }

                section.EnrolledCount--;
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    break;
                }
                catch (DbUpdateConcurrencyException) when (attempt < 5)
                {
                    // Another approval or drop touched this section first — reload and re-check.
                    await db.Entry(section).ReloadAsync(cancellationToken);
                }
            }

            return new DropOutcome(true, null);
        }

        /// <summary>
        /// Roll a failed drop back to Approved. The seat was never returned, so only the in-memory
        /// request and the notice/audit rows queued alongside it have to be undone — the same
        /// unwind <c>RevertApproval</c> does on the way in.
        /// </summary>
        private static void RevertDrop(AppDbContext db, SlotRequest request)
        {
            request.Status = SlotRequestStatus.Approved;
            request.DroppedAtUtc = null;
            request.DroppedByUserId = null;
            request.DropReason = null;
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

        private static string Suffix(string? reason) =>
            reason is null ? string.Empty : $" Reason: {reason}";

        public static Guid? CurrentUserId(ClaimsPrincipal principal) =>
            EnlistmentEligibility.CurrentUserId(principal);
    }
}
