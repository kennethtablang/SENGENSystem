using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Scheduling.Finalize
{
    // Vertical slice: the Academic Head signs off a generated/reviewed draft as "final, ready to
    // publish". Finalizing locks the draft from regeneration and board edits (enforced in those
    // slices) until it is reopened; the Registrar then publishes it (FR-SCHED-06, FR-PUB).
    // Lifecycle: Draft → Finalized → Published.
    public record FinalizeScheduleResponse(
        Guid SemesterId,
        string SemesterName,
        bool IsFinalized,
        int DraftCount,
        int FinalizedCount,
        int PublishedCount);

    public static class FinalizeScheduleEndpoint
    {
        public static IEndpointRouteBuilder MapFinalizeSchedule(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/scheduling")
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.AcademicHead), nameof(UserRole.SchoolAdmin)));

            group.MapPost("/{semesterId:guid}/finalize", FinalizeAsync);
            group.MapPost("/{semesterId:guid}/reopen", ReopenAsync);
            return app;
        }

        private static async Task<IResult> FinalizeAsync(
            Guid semesterId,
            AppDbContext db,
            AuditLog audit,
            Features.Reports.Live.ReportsBroadcaster broadcaster,
            CancellationToken ct)
        {
            var semester = await db.Semesters.FirstOrDefaultAsync(s => s.Id == semesterId, ct);
            if (semester is null) return Results.NotFound(new { message = "Semester not found." });
            if (semester.IsArchived)
                return Results.Conflict(new { message = $"“{semester.Name}” is archived — its schedule is read-only." });

            var assignments = await db.ScheduleAssignments
                .Where(a => a.SemesterId == semester.Id)
                .ToListAsync(ct);

            if (assignments.Count == 0)
            {
                return Results.BadRequest(new
                {
                    message = "There is no schedule to finalize yet. Generate or build one first."
                });
            }

            // Only unpublished rows are a "draft" to finalize; published rows are already official.
            var draft = assignments.Where(a => !a.IsPublished).ToList();
            if (draft.Count == 0)
            {
                return Results.Conflict(new
                {
                    message = "This schedule is already published — there is no draft left to finalize."
                });
            }

            var toFinalize = draft.Where(a => !a.IsFinalized).ToList();
            var now = DateTime.UtcNow;
            foreach (var a in toFinalize)
            {
                a.IsFinalized = true;
                a.FinalizedAtUtc = now;
            }

            if (toFinalize.Count > 0)
            {
                audit.Record(AuditAction.ScheduleFinalized,
                    $"Finalized the {semester.Name} schedule ({draft.Count} draft row(s)) — locked and ready to publish.",
                    "Semester", semester.Id.ToString());
                await db.SaveChangesAsync(ct);
                broadcaster.Announce("scheduling");
            }

            return Results.Ok(BuildResponse(semester, assignments));
        }

        private static async Task<IResult> ReopenAsync(
            Guid semesterId,
            AppDbContext db,
            AuditLog audit,
            Features.Reports.Live.ReportsBroadcaster broadcaster,
            CancellationToken ct)
        {
            var semester = await db.Semesters.FirstOrDefaultAsync(s => s.Id == semesterId, ct);
            if (semester is null) return Results.NotFound(new { message = "Semester not found." });
            if (semester.IsArchived)
                return Results.Conflict(new { message = $"“{semester.Name}” is archived — its schedule is read-only." });

            var assignments = await db.ScheduleAssignments
                .Where(a => a.SemesterId == semester.Id)
                .ToListAsync(ct);

            var finalized = assignments.Where(a => a.IsFinalized && !a.IsPublished).ToList();
            if (finalized.Count == 0)
            {
                return Results.Conflict(new
                {
                    message = "This schedule is not finalized, so there is nothing to reopen."
                });
            }

            foreach (var a in finalized)
            {
                a.IsFinalized = false;
                a.FinalizedAtUtc = null;
            }

            audit.Record(AuditAction.ScheduleReopened,
                $"Reopened the {semester.Name} schedule for editing ({finalized.Count} row(s) unlocked).",
                "Semester", semester.Id.ToString());
            await db.SaveChangesAsync(ct);
            broadcaster.Announce("scheduling");

            return Results.Ok(BuildResponse(semester, assignments));
        }

        private static FinalizeScheduleResponse BuildResponse(Semester semester, List<ScheduleAssignment> assignments)
        {
            var published = assignments.Count(a => a.IsPublished);
            var draft = assignments.Where(a => !a.IsPublished).ToList();
            var finalized = draft.Count(a => a.IsFinalized);
            // The draft is "finalized" as a whole only when every draft row is locked.
            var isFinalized = draft.Count > 0 && finalized == draft.Count;
            return new FinalizeScheduleResponse(
                semester.Id, semester.Name, isFinalized, draft.Count, finalized, published);
        }
    }
}
