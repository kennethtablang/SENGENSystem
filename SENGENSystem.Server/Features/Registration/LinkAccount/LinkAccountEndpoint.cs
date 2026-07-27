using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Documents;

namespace SENGENSystem.Server.Features.Registration.LinkAccount
{
    // Vertical slice: a signed-in student claims their SIS registration record, tying the
    // login account to the student number (the identity link enlistment eligibility rests on,
    // FR-ENL-05). The claim requires three matching facts — the student number (delivered only
    // to the SIS email), the account email equal to the SIS email, and the date of birth —
    // because self-service registration never verifies mailbox ownership, so no record may
    // ever bind to an account on the email string alone.
    public record LinkAccountRequest(string? StudentNumber, string? DateOfBirth);

    public record LinkedDocumentDto(
        string RequirementCode, string Label, string Status, bool GatesAuthorization)
    {
        public static LinkedDocumentDto From(RegistrationDocument d, RequirementCatalog catalog) =>
            new(d.RequirementCode,
                catalog.Label(d.RequirementCode),
                d.Status.ToString(),
                // Flagged to the student so they know which papers actually hold up their
                // clearance and which may follow (FR-PRE-02).
                catalog.GatesAuthorization(d.RequirementCode));
    }

    public record LinkedRegistrationDto(
        Guid RegistrationId,
        string StudentNumber,
        string FullName,
        string Program,
        string StudentType,
        string RegistrationStatus,
        string? SemesterName,
        bool DocumentsComplete,
        int SubmittedCount,
        int TotalCount,
        bool IsPreAuthorized,
        int YearLevel,
        string YearLevelLabel,
        // FR-EVAL: a transferee's credit evaluation stands between them and enlistment, so their
        // own dashboard has to be able to show it as a step. Null for a new student, who has no
        // such step at all.
        string? EvaluationStatus,
        int? CreditedUnits,
        int? ToTakeUnits,
        IReadOnlyList<LinkedDocumentDto> Documents)
    {
        public static LinkedRegistrationDto From(
            StudentRegistration r,
            RequirementCatalog catalog,
            Domain.TransfereeEvaluation? evaluation = null)
        {
            // Only the papers this student's route into the school actually calls for — a
            // transferee is never shown a Form 138 they cannot produce (FR-DOC-01).
            var documents = DocumentChecklist.Applicable(r, catalog);
            var isTransferee = r.StudentType == Domain.StudentType.Transferee;
            var totals = isTransferee
                ? TransfereeEvaluation.TransfereeEvaluationEndpoints.Totals(evaluation)
                : default;

            return new(
                r.Id,
                r.StudentNumber,
                r.FullName,
                r.Program.ToString(),
                r.StudentType.ToString(),
                r.Status.ToString(),
                r.Semester?.Name,
                DocumentChecklist.IsComplete(documents),
                DocumentChecklist.SubmittedCount(documents),
                documents.Count,
                r.IsPreAuthorized,
                r.YearLevel,
                YearLevelPolicy.Label(r.YearLevel),
                isTransferee
                    ? (evaluation?.Status ?? TransfereeEvaluationStatus.Pending).ToString()
                    : null,
                isTransferee ? totals.Credited : null,
                isTransferee ? totals.ToTake : null,
                documents
                    .OrderBy(d => catalog.Order(d.RequirementCode))
                    .Select(d => LinkedDocumentDto.From(d, catalog))
                    .ToList());
        }
    }

    public static class LinkAccountEndpoint
    {
        public static IEndpointRouteBuilder MapLinkAccount(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/registration/link", GetAsync)
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.Student)));
            app.MapPost("/api/registration/link", ClaimAsync)
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.Student)));
            return app;
        }

        /// <summary>
        /// A transferee's credit evaluation, or null for a new student (who never has one). Kept
        /// beside the link payload so the student's dashboard can show the evaluation as the step
        /// it is, rather than leaving them staring at a blocked enlistment with no explanation.
        /// </summary>
        private static async Task<Domain.TransfereeEvaluation?> LoadEvaluationAsync(
            AppDbContext db, StudentRegistration registration, CancellationToken ct) =>
            registration.StudentType != StudentType.Transferee
                ? null
                : await TransfereeEvaluation.TransfereeEvaluationEndpoints
                    .LoadEvaluationAsync(db, registration.Id, tracking: false, ct);

        /// <summary>The signed-in student's link state — powers the "claim your record" card.</summary>
        private static async Task<IResult> GetAsync(
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken cancellationToken)
        {
            var userId = CurrentUserId(principal);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var linked = await db.StudentRegistrations
                .AsNoTracking()
                .Include(r => r.Semester)
                .Include(r => r.Documents)
                .FirstOrDefaultAsync(r => r.UserId == userId, cancellationToken);

            if (linked is not null)
            {
                var catalog = await DocumentChecklist.LoadCatalogAsync(db, cancellationToken);
                return Results.Ok(new { linked = true, registration = LinkedRegistrationDto.From(linked, catalog, await LoadEvaluationAsync(db, linked, cancellationToken)) });
            }

            // Hint whether an unclaimed SIS record carries this account's email.
            var email = await db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(cancellationToken);
            var claimable = email is not null && await db.StudentRegistrations
                .AnyAsync(r => r.UserId == null && r.Email == email, cancellationToken);

            return Results.Ok(new { linked = false, claimable });
        }

        private static async Task<IResult> ClaimAsync(
            LinkAccountRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            CancellationToken cancellationToken)
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.StudentNumber))
            {
                errors["studentNumber"] = ["Please enter your student number (e.g. 2026-000001)."];
            }
            DateOnly dateOfBirth = default;
            if (string.IsNullOrWhiteSpace(request.DateOfBirth)
                || !DateOnly.TryParse(request.DateOfBirth, out dateOfBirth))
            {
                errors["dateOfBirth"] = ["Please enter your date of birth as it appears on your SIS."];
            }
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var userId = CurrentUserId(principal);
            var user = userId is { } id
                ? await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
                : null;
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var existing = await db.StudentRegistrations
                .FirstOrDefaultAsync(r => r.UserId == user.Id, cancellationToken);

            var studentNumber = request.StudentNumber.Trim();
            var registration = await db.StudentRegistrations
                .Include(r => r.Semester)
                .Include(r => r.Documents)
                .FirstOrDefaultAsync(r => r.StudentNumber == studentNumber, cancellationToken);

            if (registration is null)
            {
                return Results.NotFound(new
                {
                    message = "No SIS registration was found for that student number. " +
                              "Check the number on your registration confirmation email."
                });
            }

            var catalog = await DocumentChecklist.LoadCatalogAsync(db, cancellationToken);

            if (registration.UserId == user.Id)
            {
                return Results.Ok(new { linked = true, registration = LinkedRegistrationDto.From(registration, catalog, await LoadEvaluationAsync(db, registration, cancellationToken)) });
            }

            if (existing is not null)
            {
                return Results.Conflict(new
                {
                    message = $"Your account is already linked to student number {existing.StudentNumber}."
                });
            }

            if (registration.UserId is not null)
            {
                return Results.Conflict(new
                {
                    message = "That student record has already been claimed by another account. " +
                              "If this is your record, please contact the Registrar."
                });
            }

            // Both facts must match; the error deliberately does not say which one failed,
            // so the endpoint cannot be used to confirm another student's details.
            if (!string.Equals(registration.Email, user.Email, StringComparison.OrdinalIgnoreCase)
                || registration.DateOfBirth != dateOfBirth)
            {
                return Results.BadRequest(new
                {
                    message = "The details you entered do not match that SIS record. Make sure your " +
                              "account email and date of birth are exactly as on your SIS, or contact the Registrar."
                });
            }

            registration.UserId = user.Id;
            audit.Record(AuditAction.StudentAccountLinked,
                $"Linked account {user.Email} to SIS record {registration.StudentNumber}.",
                "StudentRegistration", registration.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new { linked = true, registration = LinkedRegistrationDto.From(registration, catalog, await LoadEvaluationAsync(db, registration, cancellationToken)) });
        }

        private static Guid? CurrentUserId(ClaimsPrincipal principal)
        {
            var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }
}
