using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Registration.TermActivation
{
    // Vertical slice: the institution-wide switch for returning-student term activation. Activation
    // is a self-service public form, so between terms it has to be closable — otherwise students
    // file for a term that is not open yet and the Admission Office spends the gap rejecting them.
    //
    // Who holds the switch: the Registrar and the Academic Head, who own the enrollment cycle, plus
    // the two administrator roles. (The two admin roles reach this through the all-roles claims
    // transformation rather than by being named — SchoolAdmin is listed anyway so the intent reads
    // off the endpoint instead of out of a transformation elsewhere.) The Admission Officer, who
    // validates the requests, deliberately does not decide when the window opens.
    public record TermActivationControlRequest(bool? Open);

    public static class TermActivationControlEndpoints
    {
        public static IEndpointRouteBuilder MapTermActivationControl(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/registration/term-activation/control")
                .RequireAuthorization(policy => policy.RequireRole(
                    nameof(UserRole.Registrar), nameof(UserRole.AcademicHead), nameof(UserRole.SchoolAdmin)));

            group.MapGet("", GetAsync);
            group.MapPut("", SetAsync);
            return app;
        }

        private static async Task<IResult> GetAsync(AppDbContext db, CancellationToken ct) =>
            Results.Ok(await StateAsync(db, ct));

        private static async Task<IResult> SetAsync(
            TermActivationControlRequest request, AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            if (request.Open is not { } open)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["open"] = ["Say whether term activation should be open or closed."]
                });
            }

            var settings = await db.GetSettingsForUpdateAsync(ct);
            if (settings.TermActivationOpen != open)
            {
                settings.TermActivationOpen = open;
                settings.UpdatedAtUtc = DateTime.UtcNow;
                audit.Record(AuditAction.SystemParametersUpdated,
                    open
                        ? "Opened returning-student term activation."
                        : "Closed returning-student term activation.",
                    "SystemSettings", SystemSettings.SingletonId.ToString());
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(await StateAsync(db, ct));
        }

        /// <summary>
        /// The switch plus the context needed to decide whether to throw it: which term requests
        /// would land in, and how many are already waiting on the Admission Office.
        /// </summary>
        private static async Task<object> StateAsync(AppDbContext db, CancellationToken ct)
        {
            var settings = await db.GetSettingsAsync(ct);
            var semester = await db.Semesters.AsNoTracking()
                .FirstOrDefaultAsync(s => s.IsActive, ct);

            var pending = semester is null
                ? 0
                : await db.TermActivations.AsNoTracking()
                    .CountAsync(a => a.SemesterId == semester.Id && a.Status == TermActivationStatus.Pending, ct);

            var total = semester is null
                ? 0
                : await db.TermActivations.AsNoTracking()
                    .CountAsync(a => a.SemesterId == semester.Id, ct);

            return new
            {
                open = settings.TermActivationOpen,
                semesterId = semester?.Id,
                semesterName = semester?.Name,
                pendingCount = pending,
                requestCount = total,
                updatedAtUtc = Iso.Utc(settings.UpdatedAtUtc)
            };
        }
    }
}
