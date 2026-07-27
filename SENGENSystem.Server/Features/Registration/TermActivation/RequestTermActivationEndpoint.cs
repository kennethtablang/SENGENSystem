using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Registration.TermActivation
{
    // Vertical slice: a returning student self-requests activation for the active term via a public
    // lookup (student number + last name). No SIS re-entry; an Admission Officer validates it later.
    //
    // The number asked for is the **official student number** — the one on the student's ID, issued
    // by the student-records system and recorded here by the Admission Officer. A returning student
    // has carried that number for years; SEN-GEN's own registration number is an internal artifact
    // of the term they first enrolled and is not something they would have to hand. The registration
    // number is still accepted as a fallback so a student who only ever received that one (a recent
    // enrollee not yet issued an official number) is not locked out.
    //
    // This is step two: the student has already been shown their year level and the term on the
    // lookup (see LookupTermActivationEndpoint) and now confirms both. The confirmation is not
    // ceremony — SemesterId is checked against the live active term so a form left open across a
    // term rollover cannot file into the wrong one, and the confirmed year level is filed with the
    // request for the Admission Officer to weigh against the derived one.
    public record RequestTermActivationRequest(
        string? StudentNumber,
        string? LastName,
        Guid? SemesterId,
        int? YearLevel,
        bool? Confirmed);

    public record RequestTermActivationResponse(
        Guid Id,
        string StudentNumber,
        string Status,
        string SemesterName,
        int DeclaredYearLevel,
        string DeclaredYearLevelLabel);

    public static class RequestTermActivationEndpoint
    {
        public static IEndpointRouteBuilder MapRequestTermActivation(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/registration/term-activation", HandleAsync).AllowAnonymous();
            return app;
        }

        private static async Task<IResult> HandleAsync(
            RequestTermActivationRequest request,
            AppDbContext db,
            AuditLog audit,
            IEmailSender email,
            Notifier notifier,
            CancellationToken cancellationToken)
        {
            var settings = await db.GetSettingsAsync(cancellationToken);
            if (!settings.TermActivationOpen)
            {
                return TermActivationIdentity.Closed();
            }

            var (registration, problem) = await TermActivationIdentity.ResolveAsync(
                request.StudentNumber, request.LastName, db, cancellationToken);
            if (registration is null) return problem!;

            var semester = await db.Semesters.FirstOrDefaultAsync(s => s.IsActive, cancellationToken);
            if (semester is null)
            {
                return Results.BadRequest(new { message = "Term activation is closed: no active semester is set." });
            }

            // The confirmation step. Anything missing here means the request did not come through
            // the check the student is supposed to have made, so it is a validation failure rather
            // than something to guess a default for.
            var errors = new Dictionary<string, string[]>();
            if (request.Confirmed != true)
            {
                errors["confirmed"] = ["Please confirm your year level and term before finalizing."];
            }
            if (request.YearLevel is not { } declaredYearLevel || !YearLevelPolicy.IsValid(declaredYearLevel))
            {
                errors["yearLevel"] =
                    [$"Choose the year level you are coming back into ({YearLevelPolicy.MinYearLevel}–{YearLevelPolicy.MaxYearLevel})."];
                declaredYearLevel = registration.YearLevel;
            }
            // A form opened before a term rollover would otherwise file silently into the new term.
            // Refusing sends the student back through the lookup, where they see the term that is
            // actually open now.
            if (request.SemesterId is not { } confirmedSemesterId || confirmedSemesterId != semester.Id)
            {
                errors["semesterId"] =
                    [$"The term you were shown has changed — activation is now for {semester.Name}. Please check your details again."];
            }
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var alreadyActive = await db.TermActivations.AnyAsync(
                a => a.StudentRegistrationId == registration.Id
                     && a.SemesterId == semester.Id
                     && a.Status != TermActivationStatus.Rejected,
                cancellationToken);
            if (alreadyActive)
            {
                return Results.Conflict(new { message = $"You already have a term activation on file for {semester.Name}." });
            }

            var activation = new Domain.TermActivation
            {
                StudentRegistrationId = registration.Id,
                SemesterId = semester.Id,
                Status = TermActivationStatus.Pending,
                DeclaredYearLevel = declaredYearLevel
            };
            db.TermActivations.Add(activation);
            audit.RecordAnonymous(AuditAction.TermActivationRequested,
                $"Requested term activation for {semester.Name}, confirming " +
                $"{YearLevelPolicy.Label(declaredYearLevel)}" +
                (declaredYearLevel == registration.YearLevel
                    ? "."
                    : $" (their record currently reads {YearLevelPolicy.Label(registration.YearLevel)})."),
                registration.FullName, "TermActivation", activation.Id.ToString());

            // Put the request on every back-office bell (the Admission Office validates it), committed
            // with the activation (FR-NOTIF).
            var staffIds = await NotificationRecipients.StaffUserIdsAsync(db, cancellationToken);
            notifier.NotifyMany(staffIds, NotificationKind.TermActivation,
                "New term-activation request",
                $"{registration.FullName} ({registration.StudentNumber}) requested activation for {semester.Name}.",
                "/term-activations");

            await db.SaveChangesAsync(cancellationToken);

            // Receipt/proof that the request was filed — sent on request, not only on approval
            // (the approval confirmation is a separate email). Best-effort: the request is already
            // committed, so a mail failure must not fail the response.
            var (subject, body) = RegistrationEmails.TermActivationRequested(registration, semester.Name);
            var result = await email.SendAsync(registration.Email, registration.FullName, subject, body, cancellationToken);
            if (result.Sent)
            {
                audit.RecordAnonymous(AuditAction.NotificationDispatched,
                    $"Sent term activation request receipt to {registration.Email}.",
                    registration.FullName, "TermActivation", activation.Id.ToString());
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.Created($"/api/registration/term-activation/{activation.Id}",
                new RequestTermActivationResponse(
                    activation.Id,
                    // Echo back the number they identified themselves with, not the internal one.
                    registration.OfficialStudentNumber ?? registration.StudentNumber,
                    activation.Status.ToString(),
                    semester.Name,
                    declaredYearLevel,
                    YearLevelPolicy.Label(declaredYearLevel)));
        }
    }
}
