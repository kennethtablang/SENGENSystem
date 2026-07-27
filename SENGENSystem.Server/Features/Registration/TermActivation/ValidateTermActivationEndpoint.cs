using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Registration.TermActivation
{
    // Vertical slice: the Admission Officer validates (approves) or rejects a returning student's
    // term-activation request. Approval emails the student a confirmation (FR-NOTIF-01) and is also
    // where the student's year level for the coming term is settled (FR-SIS-09): a returning student
    // moves up a year when the school year turns over, and stays put within it. `YearLevel` on the
    // request overrides that derivation when the officer knows better — a repeating student, or one
    // returning after a leave.
    public record ValidateTermActivationRequest(bool Approve, string? Remarks, int? YearLevel = null);

    public static class ValidateTermActivationEndpoint
    {
        public static IEndpointRouteBuilder MapValidateTermActivation(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/registration/term-activation/{id:guid}/validate", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.AdmissionOfficer)));
            return app;
        }

        private static async Task<IResult> HandleAsync(
            Guid id,
            ValidateTermActivationRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            AuditLog audit,
            IEmailSender email,
            Notifier notifier,
            Microsoft.AspNetCore.Identity.IPasswordHasher<Domain.User> passwordHasher,
            CancellationToken cancellationToken)
        {
            var activation = await db.TermActivations
                .Include(a => a.StudentRegistration).ThenInclude(r => r!.Semester)
                .Include(a => a.Semester)
                .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

            if (activation is null)
            {
                return Results.NotFound(new { message = "Term activation request not found." });
            }

            if (activation.Status != TermActivationStatus.Pending)
            {
                return Results.Conflict(new { message = $"This request has already been {activation.Status.ToString().ToLowerInvariant()}." });
            }

            if (request.YearLevel is { } requestedYear && !YearLevelPolicy.IsValid(requestedYear))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["yearLevel"] =
                        [$"Year level must be between {YearLevelPolicy.MinYearLevel} and {YearLevelPolicy.MaxYearLevel}."]
                });
            }

            Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var officerId);

            activation.Status = request.Approve ? TermActivationStatus.Validated : TermActivationStatus.Rejected;
            activation.ValidatedByUserId = officerId == Guid.Empty ? null : officerId;
            activation.ValidatedAtUtc = DateTime.UtcNow;
            activation.Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();

            var registration = activation.StudentRegistration!;
            var semesterName = activation.Semester?.Name ?? "the active term";

            var verb = request.Approve ? "Validated" : "Rejected";
            audit.Record(AuditAction.TermActivationValidated,
                $"{verb} {registration.FullName}'s term activation for {semesterName}" +
                (activation.Remarks is null ? "." : $" — {activation.Remarks}"),
                "TermActivation", activation.Id.ToString());

            // FR-SIS-09: validating the activation is what settles the year the student comes back
            // into. Advancing turns on the *school year* changing, not the semester — a student
            // activating for the 2nd semester of the year they are already in stays in that year.
            var assignedYearLevel = registration.YearLevel;
            if (request.Approve)
            {
                var isNewSchoolYear = activation.Semester is { } target
                    && registration.Semester is { } previous
                    && target.SchoolYearId != previous.SchoolYearId;
                assignedYearLevel = request.YearLevel
                    ?? YearLevelPolicy.OnTermActivation(registration.YearLevel, isNewSchoolYear);

                if (assignedYearLevel != registration.YearLevel || request.YearLevel is not null)
                {
                    var previousYearLevel = registration.YearLevel;
                    registration.YearLevel = assignedYearLevel;
                    registration.YearLevelSetAtUtc = DateTime.UtcNow;
                    registration.YearLevelSetByUserId = officerId == Guid.Empty ? null : officerId;

                    audit.Record(AuditAction.YearLevelAssigned,
                        $"Set {registration.FullName} ({registration.StudentNumber}) to " +
                        $"{YearLevelPolicy.Label(assignedYearLevel)} on term activation for {semesterName}" +
                        (previousYearLevel == assignedYearLevel
                            ? " (unchanged)."
                            : $" (was {YearLevelPolicy.Label(previousYearLevel)})") +
                        (request.YearLevel is null ? string.Empty : " — set by the Admission Officer."),
                        "StudentRegistration", registration.Id.ToString());
                }
            }

            // Announce the decision on the back-office bells, and to the student's account, in the
            // same transaction (FR-NOTIF).
            var staffIds = await NotificationRecipients.StaffUserIdsAsync(db, cancellationToken);
            notifier.NotifyMany(staffIds, NotificationKind.TermActivation,
                $"Term activation {verb.ToLowerInvariant()}",
                $"{registration.FullName} ({registration.StudentNumber})'s activation for {semesterName} was {verb.ToLowerInvariant()}.",
                "/term-activations");
            if (registration.UserId is { } studentUserId)
            {
                notifier.Notify(studentUserId, NotificationKind.TermActivation,
                    request.Approve ? "You're activated for the term" : "Term activation not approved",
                    request.Approve
                        ? $"Your activation for {semesterName} is confirmed — you are enrolled as "
                          + $"{YearLevelPolicy.Label(assignedYearLevel)}. You may proceed with enrollment."
                        : $"Your activation for {semesterName} was not approved" +
                          (activation.Remarks is null ? "." : $": {activation.Remarks}"),
                    "/schedule");
            }

            await db.SaveChangesAsync(cancellationToken);

            var emailSent = false;
            if (request.Approve)
            {
                // Make sure the returning student has a usable login. New SIS submissions are
                // provisioned an account up front, but records that predate that (or were seeded)
                // may have none — create one now from their SIS details, forcing a first-login
                // password change, and mail the credentials alongside the confirmation.
                var provision = await Common.Auth.StudentAccountProvisioner.EnsureAsync(
                    registration, db, passwordHasher, cancellationToken);
                if (provision.Created)
                {
                    audit.Record(AuditAction.StudentAccountProvisioned,
                        $"Provisioned a student login ({provision.User.Email}) on term activation for {semesterName}.",
                        "User", provision.User.Id.ToString());
                    await db.SaveChangesAsync(cancellationToken);
                }

                var (subject, body) = RegistrationEmails.TermActivationConfirmation(registration, semesterName);
                var result = await email.SendAsync(registration.Email, registration.FullName, subject, body, cancellationToken);
                emailSent = result.Sent;
                if (result.Sent)
                {
                    audit.Record(AuditAction.NotificationDispatched,
                        $"Sent term activation confirmation to {registration.Email}.",
                        "TermActivation", activation.Id.ToString());
                    await db.SaveChangesAsync(cancellationToken);
                }

                if (provision is { Created: true, TemporaryPassword: { } temporaryPassword })
                {
                    var (credSubject, credBody) =
                        RegistrationEmails.AccountCredentials(registration, provision.User.Email, temporaryPassword);
                    await email.SendAsync(
                        registration.Email, registration.FullName, credSubject, credBody, cancellationToken);
                }
            }

            return Results.Ok(new
            {
                id = activation.Id,
                status = activation.Status.ToString(),
                yearLevel = registration.YearLevel,
                yearLevelLabel = YearLevelPolicy.Label(registration.YearLevel),
                emailSent
            });
        }
    }
}
