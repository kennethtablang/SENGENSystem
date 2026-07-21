using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.EnrollmentCycle
{
    // Vertical slice: the active term's enrollment stage — the banner every signed-in user sees
    // in the top bar, and the Registrar's control for moving the term to the next phase.
    // Everyone may read it (it tells students what they can do right now); only the Registrar
    // and School Admin may change it.
    public record SetStageRequest(string? Stage);

    public static class EnrollmentStageEndpoints
    {
        /// <summary>The cycle in order. Advancing means "the next one along this list".</summary>
        private static readonly EnrollmentStage[] Order =
        [
            EnrollmentStage.Preparation,
            EnrollmentStage.DocumentSubmission,
            EnrollmentStage.Registration,
            EnrollmentStage.Enlistment,
            EnrollmentStage.Closed
        ];

        public static IEndpointRouteBuilder MapEnrollmentStage(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/enrollment-stage").RequireAuthorization();

            group.MapGet("", GetAsync);
            group.MapPost("", SetAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.Registrar), nameof(UserRole.SchoolAdmin)));
            group.MapPost("/advance", AdvanceAsync)
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.Registrar), nameof(UserRole.SchoolAdmin)));
            return app;
        }

        private static async Task<IResult> GetAsync(AppDbContext db, HttpContext http, CancellationToken ct)
        {
            var semester = await db.Semesters.AsNoTracking()
                .FirstOrDefaultAsync(s => s.IsActive, ct);
            return Results.Ok(Payload(semester, http));
        }

        // POST /api/enrollment-stage — set a specific stage. Used for corrections (including
        // stepping back), where /advance only ever moves forward one place.
        private static async Task<IResult> SetAsync(
            SetStageRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            if (!Enum.TryParse<EnrollmentStage>(request.Stage, ignoreCase: true, out var target)
                || !Order.Contains(target))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["stage"] = ["Choose one of the enrollment stages."]
                });
            }
            return await ApplyAsync(target, db, audit, ct);
        }

        // POST /api/enrollment-stage/advance — step to the next stage in the cycle.
        private static async Task<IResult> AdvanceAsync(AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var semester = await db.Semesters.FirstOrDefaultAsync(s => s.IsActive, ct);
            if (semester is null) return NoActiveSemester();

            var index = Array.IndexOf(Order, semester.EnrollmentStage);
            if (index < 0 || index == Order.Length - 1)
            {
                return Results.Conflict(new
                {
                    message = $"{semester.Name} is already at the final stage ({Label(semester.EnrollmentStage)})."
                });
            }
            return await ApplyAsync(Order[index + 1], db, audit, ct);
        }

        private static async Task<IResult> ApplyAsync(
            EnrollmentStage target, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var semester = await db.Semesters.FirstOrDefaultAsync(s => s.IsActive, ct);
            if (semester is null) return NoActiveSemester();

            if (semester.EnrollmentStage == target)
            {
                return Results.Conflict(new
                {
                    message = $"{semester.Name} is already in {Label(target)}."
                });
            }

            var previous = semester.EnrollmentStage;
            semester.EnrollmentStage = target;

            audit.Record(AuditAction.EnrollmentStageChanged,
                $"Moved {semester.Name} from {Label(previous)} to {Label(target)}.",
                "Semester", semester.Id.ToString());
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                semesterId = semester.Id,
                semesterName = semester.Name,
                stage = semester.EnrollmentStage.ToString(),
                stageLabel = Label(semester.EnrollmentStage),
                previousStage = previous.ToString()
            });
        }

        private static IResult NoActiveSemester() =>
            Results.Conflict(new { message = "No semester is active — activate one under Academic setup first." });

        private static object Payload(Semester? semester, HttpContext http)
        {
            var canChange = http.User.IsInRole(nameof(UserRole.Registrar))
                || http.User.IsInRole(nameof(UserRole.SchoolAdmin));

            if (semester is null)
            {
                return new
                {
                    semesterId = (Guid?)null,
                    semesterName = (string?)null,
                    stage = (string?)null,
                    canChange,
                    stages = Order.Select(s => new { value = s.ToString(), label = Label(s) }).ToList()
                };
            }

            var index = Array.IndexOf(Order, semester.EnrollmentStage);
            var next = index >= 0 && index < Order.Length - 1 ? Order[index + 1] : (EnrollmentStage?)null;

            return new
            {
                semesterId = semester.Id,
                semesterName = semester.Name,
                term = semester.Term.ToString(),
                stage = semester.EnrollmentStage.ToString(),
                stageLabel = Label(semester.EnrollmentStage),
                stageIndex = index,
                nextStage = next?.ToString(),
                nextStageLabel = next is null ? null : Label(next.Value),
                canChange,
                stages = Order.Select(s => new { value = s.ToString(), label = Label(s) }).ToList()
            };
        }

        /// <summary>Display names for the stages — the API owns the wording so every screen agrees.</summary>
        private static string Label(EnrollmentStage stage) => stage switch
        {
            EnrollmentStage.Preparation => "Preparation",
            EnrollmentStage.DocumentSubmission => "Document submission",
            EnrollmentStage.Registration => "Registration",
            EnrollmentStage.Enlistment => "Subject enlistment",
            EnrollmentStage.Closed => "Enrollment closed",
            _ => stage.ToString()
        };
    }
}
