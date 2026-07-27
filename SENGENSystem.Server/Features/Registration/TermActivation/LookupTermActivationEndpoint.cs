using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Registration.TermActivation
{
    // Vertical slice: step one of term activation. A returning student identifies themselves, and
    // gets back the two facts they are being asked to agree to — the year level they are coming
    // back into and the term they are activating for — before anything is filed.
    //
    // Why a step of its own: activation used to be a single submit, which meant a student found out
    // what they had activated into only from the confirmation email, and a student who had been
    // promoted (or held back) by mistake had no moment to notice. The check happens where it is
    // cheap, and the year level they confirmed is filed with the request as the student's own
    // answer for the Admission Officer to weigh (TermActivation.DeclaredYearLevel).
    public record LookupTermActivationRequest(string? StudentNumber, string? LastName);

    public static class LookupTermActivationEndpoint
    {
        public static IEndpointRouteBuilder MapLookupTermActivation(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/registration/term-activation/lookup", HandleAsync).AllowAnonymous();
            return app;
        }

        private static async Task<IResult> HandleAsync(
            LookupTermActivationRequest request,
            AppDbContext db,
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

            var semester = await db.Semesters.AsNoTracking()
                .Include(s => s.SchoolYear)
                .FirstOrDefaultAsync(s => s.IsActive, cancellationToken);
            if (semester is null)
            {
                return Results.BadRequest(new { message = "Term activation is closed: no active semester is set." });
            }

            // The record's own term is what tells us whether the school year has turned over — the
            // same test the Admission Officer's validation applies, shown to the student first so
            // the number they confirm is the number the office is going to derive.
            var previousSchoolYearId = await db.Semesters.AsNoTracking()
                .Where(s => s.Id == registration.SemesterId)
                .Select(s => s.SchoolYearId)
                .FirstOrDefaultAsync(cancellationToken);
            var isNewSchoolYear = semester.SchoolYearId != previousSchoolYearId;
            var proposed = YearLevelPolicy.OnTermActivation(registration.YearLevel, isNewSchoolYear);

            var existing = await db.TermActivations.AsNoTracking()
                .Where(a => a.StudentRegistrationId == registration.Id
                    && a.SemesterId == semester.Id
                    && a.Status != TermActivationStatus.Rejected)
                .Select(a => a.Status)
                .FirstOrDefaultAsync(cancellationToken);
            var alreadyFiled = existing != default;

            return Results.Ok(new
            {
                // Echo back the number they identified themselves with, not the internal one.
                studentNumber = registration.OfficialStudentNumber ?? registration.StudentNumber,
                fullName = registration.FullName,
                program = registration.Program.ToString(),
                studentType = registration.StudentType.ToString(),
                currentYearLevel = registration.YearLevel,
                currentYearLevelLabel = YearLevelPolicy.Label(registration.YearLevel),
                proposedYearLevel = proposed,
                proposedYearLevelLabel = YearLevelPolicy.Label(proposed),
                isNewSchoolYear,
                minYearLevel = YearLevelPolicy.MinYearLevel,
                maxYearLevel = YearLevelPolicy.MaxYearLevel,
                semesterId = semester.Id,
                semesterName = semester.Name,
                termLabel = TermActivationIdentity.TermLabel(semester.Term),
                schoolYearName = semester.SchoolYear?.Name,
                alreadyFiled,
                existingStatus = alreadyFiled ? existing.ToString() : null
            });
        }
    }

    /// <summary>
    /// The identity check both legs of term activation share: student number plus last name, with
    /// one deliberately vague failure so the endpoint never confirms which student numbers exist.
    /// </summary>
    internal static class TermActivationIdentity
    {
        public static IResult Closed() => Results.Json(new
        {
            message = "Term activation is currently closed. Please check back once the school reopens it, "
                    + "or contact the Admission Office."
        }, statusCode: StatusCodes.Status403Forbidden);

        public static async Task<(StudentRegistration? Registration, IResult? Problem)> ResolveAsync(
            string? studentNumber, string? lastName, AppDbContext db, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(studentNumber) || string.IsNullOrWhiteSpace(lastName))
            {
                return (null, Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["studentNumber"] = string.IsNullOrWhiteSpace(studentNumber) ? ["Your student number is required."] : [],
                    ["lastName"] = string.IsNullOrWhiteSpace(lastName) ? ["Your last name is required."] : []
                }));
            }

            var number = studentNumber.Trim();
            var surname = lastName.Trim();

            // Official student number first — that is what the form asks for and what the student
            // has on their ID. The registration number is the fallback for anyone never issued one.
            var registration = await db.StudentRegistrations
                .FirstOrDefaultAsync(r => r.OfficialStudentNumber == number, cancellationToken)
                ?? await db.StudentRegistrations
                    .FirstOrDefaultAsync(r => r.StudentNumber == number, cancellationToken);

            // Same generic message whether the number is unknown or the name doesn't match — don't
            // confirm which student numbers exist.
            if (registration is null
                || !string.Equals(registration.LastName, surname, StringComparison.OrdinalIgnoreCase))
            {
                return (null, Results.NotFound(new
                {
                    message = "We couldn't find a matching student record. Check your student number and last name."
                }));
            }

            return (registration, null);
        }

        public static string TermLabel(SemesterTerm term) =>
            term == SemesterTerm.SecondSemester ? "Second Semester" : "First Semester";
    }
}
