using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Enlistment.SeatCounts
{
    public sealed record SeatCountMismatchDto(
        Guid SectionId,
        string SectionCode,
        string SubjectCode,
        int Capacity,
        int EnrolledCount,
        int ApprovedRequests);

    /// <summary>
    /// F-08 follow-up: <see cref="Section.EnrolledCount"/> is a stored counter, and before
    /// <see cref="SeatRelease"/> existed it could only ever go up — so counts that drifted then are
    /// still wrong now. This finds every section whose counter disagrees with the number of live
    /// approved requests actually holding a seat in it, and lets staff correct one on purpose.
    /// <para>
    /// <b>It surfaces; it does not silently correct.</b> A mismatch means something happened that
    /// the seat lifecycle did not record, and a counter quietly rewritten on read would hide that
    /// for good. Staff see the figures side by side and choose.
    /// </para>
    /// <para>
    /// The correction is the one write to the counter outside <c>TryApproveAsync</c> and
    /// <see cref="SeatRelease"/>, and it is not an exception to their invariant so much as its
    /// repair: they move a seat, this re-states how many seats are already taken. It rides the same
    /// <see cref="Section.RowVersion"/> retry, recounting inside the loop, so an approval or drop
    /// racing it cannot leave the figure wrong in the other direction.
    /// </para>
    /// </summary>
    public static class SeatCountEndpoints
    {
        public static IEndpointRouteBuilder MapSeatCounts(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/enlistment/seat-counts")
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.Registrar), nameof(UserRole.SchoolAdmin)));
            group.MapGet("mismatches", ListMismatchesAsync);
            group.MapPost("{sectionId:guid}/reconcile", ReconcileAsync);
            return app;
        }

        // Filtered on an anonymous projection and only then mapped to the record: EF cannot
        // translate a WHERE over a positional record's constructor, which failed at runtime against
        // SQL Server while passing happily in memory.
        internal static async Task<List<SeatCountMismatchDto>> MismatchesAsync(
            AppDbContext db, Guid semesterId, CancellationToken ct) =>
            (await db.Sections.AsNoTracking()
                .Where(s => s.SemesterId == semesterId)
                .Select(s => new
                {
                    s.Id,
                    s.SectionCode,
                    SubjectCode = s.Subject != null ? s.Subject.Code : string.Empty,
                    s.Capacity,
                    s.EnrolledCount,
                    Approved = db.SlotRequests.Count(r => r.SectionId == s.Id && r.Status == SlotRequestStatus.Approved)
                })
                .Where(m => m.EnrolledCount != m.Approved)
                .OrderBy(m => m.SectionCode)
                .ToListAsync(ct))
            .Select(m => new SeatCountMismatchDto(m.Id, m.SectionCode, m.SubjectCode, m.Capacity, m.EnrolledCount, m.Approved))
            .ToList();

        private static async Task<IResult> ListMismatchesAsync(Guid? semesterId, AppDbContext db, CancellationToken ct)
        {
            var sid = semesterId ?? await db.GetActiveSemesterIdAsync(ct);
            if (sid is null)
            {
                return Results.Ok(new { count = 0, sections = Array.Empty<SeatCountMismatchDto>() });
            }

            var sections = await MismatchesAsync(db, sid.Value, ct);
            return Results.Ok(new { count = sections.Count, sections });
        }

        private static async Task<IResult> ReconcileAsync(
            Guid sectionId, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var section = await db.Sections.Include(s => s.Subject).FirstOrDefaultAsync(s => s.Id == sectionId, ct);
            if (section is null) return Results.NotFound(new { message = "Section not found." });

            for (var attempt = 1; ; attempt++)
            {
                var approved = await db.SlotRequests
                    .CountAsync(r => r.SectionId == sectionId && r.Status == SlotRequestStatus.Approved, ct);
                var before = section.EnrolledCount;

                if (approved == before)
                {
                    return Results.Ok(new { sectionId, enrolledCount = before, changed = false });
                }

                // CK_Sections_EnrolledCount holds EnrolledCount <= Capacity. More live approvals
                // than seats means the capacity was lowered under them — that is a decision for a
                // person (raise the cap or drop a student), not something a recount can settle.
                if (approved > section.Capacity)
                {
                    return Results.Conflict(new
                    {
                        message = $"{section.SectionCode} has {approved} approved students but only " +
                                  $"{section.Capacity} seats. Raise its capacity or drop a student first, " +
                                  "then reconcile."
                    });
                }

                section.EnrolledCount = approved;
                audit.Record(AuditAction.SeatCountReconciled,
                    $"Reconciled the seat count of {section.SectionCode} ({section.Subject?.Code}): " +
                    $"{before} → {approved}, the number of live approved requests.",
                    "Section", section.Id.ToString());
                try
                {
                    await db.SaveChangesAsync(ct);
                    return Results.Ok(new { sectionId, enrolledCount = approved, changed = true, before });
                }
                catch (DbUpdateConcurrencyException) when (attempt < 5)
                {
                    // An approval or drop touched this section first — reload and recount. The
                    // audit entry staged for the lost attempt goes too, or the retry would record
                    // the reconciliation twice with two different "before" figures.
                    audit.DiscardUnsaved();
                    await db.Entry(section).ReloadAsync(ct);
                }
            }
        }
    }
}
