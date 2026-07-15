using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Common.Validation;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Registration.RegisterStudent
{
    // Vertical slice: a new student / transferee self-submits the digital SIS on a public,
    // account-less form (FR-SIS-01..03). Enum fields arrive as strings (parsed + validated here),
    // matching the rest of the API. On success we issue a student number, seed the document
    // checklist, and email a confirmation (FR-SIS-05).
    public record RegisterStudentRequest(
        string? StudentType,
        string? Program,
        string? LastName,
        string? FirstName,
        string? MiddleName,
        string? DateOfBirth,
        string? Birthplace,
        string? Citizenship,
        string? CivilStatus,
        string? Gender,
        string? Email,
        string? MobileNumber,
        string? AddressLine,
        string? Barangay,
        string? CityMunicipality,
        string? Province,
        string? ZipCode,
        string? LastSchoolLevel,
        string? SchoolName,
        string? SchoolProgram,
        string? SchoolYear,
        string? YearGradeLastAttended,
        string? LastTerm,
        string? FatherName,
        string? FatherMobile,
        string? MotherName,
        string? MotherMobile,
        string? GuardianRelationship,
        string? GuardianName,
        string? GuardianMobile,
        string? ReferredBy,
        bool AcceptedTerms);

    public record RegisterStudentResponse(Guid Id, string StudentNumber, string FullName, bool EmailSent);

    public static class RegisterStudentEndpoint
    {
        public static IEndpointRouteBuilder MapRegisterStudent(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/registration", HandleAsync).AllowAnonymous();
            return app;
        }

        private static async Task<IResult> HandleAsync(
            RegisterStudentRequest request,
            AppDbContext db,
            AuditLog audit,
            IEmailSender email,
            CancellationToken cancellationToken)
        {
            var semester = await db.Semesters.FirstOrDefaultAsync(s => s.IsActive, cancellationToken);
            if (semester is null)
            {
                return Results.BadRequest(new { message = "Registration is closed: no active semester is set." });
            }

            var errors = new Dictionary<string, string[]>();

            // ---- Required text ----
            RequireText(errors, "lastName", request.LastName);
            RequireText(errors, "firstName", request.FirstName);
            RequireText(errors, "birthplace", request.Birthplace);
            RequireText(errors, "citizenship", request.Citizenship);
            RequireText(errors, "mobileNumber", request.MobileNumber);
            RequireText(errors, "addressLine", request.AddressLine);
            RequireText(errors, "barangay", request.Barangay);
            RequireText(errors, "cityMunicipality", request.CityMunicipality);
            RequireText(errors, "province", request.Province);
            RequireText(errors, "schoolName", request.SchoolName);
            RequireText(errors, "guardianName", request.GuardianName);
            RequireText(errors, "guardianMobile", request.GuardianMobile);

            if (string.IsNullOrWhiteSpace(request.Email) || !MailAddress.TryCreate(request.Email.Trim(), out _))
            {
                errors["email"] = ["A valid email address is required so we can send your confirmation."];
            }

            // ---- Enums ----
            var studentType = ParseEnum<StudentType>(errors, "studentType", request.StudentType);
            var program = ParseEnum<ProgramTrack>(errors, "program", request.Program);
            var civilStatus = ParseEnum<CivilStatus>(errors, "civilStatus", request.CivilStatus);
            var gender = ParseEnum<Gender>(errors, "gender", request.Gender);
            var lastSchoolLevel = ParseEnum<LastSchoolLevel>(errors, "lastSchoolLevel", request.LastSchoolLevel);
            var yearGrade = ParseEnum<YearGradeLevel>(errors, "yearGradeLastAttended", request.YearGradeLastAttended);
            var lastTerm = ParseEnum<AcademicTerm>(errors, "lastTerm", request.LastTerm);
            var guardianRel = ParseEnum<GuardianRelationship>(errors, "guardianRelationship", request.GuardianRelationship);

            // ---- Date of birth ----
            DateOnly dob = default;
            if (string.IsNullOrWhiteSpace(request.DateOfBirth) || !DateOnly.TryParse(request.DateOfBirth, out dob))
            {
                errors["dateOfBirth"] = ["A valid date of birth is required."];
            }

            if (!request.AcceptedTerms)
            {
                errors["acceptedTerms"] = ["You must read and accept the terms and conditions to register."];
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var registration = new StudentRegistration
            {
                StudentNumber = await GenerateStudentNumberAsync(db, semester, cancellationToken),
                Status = RegistrationStatus.Submitted,
                StudentType = studentType,
                Program = program,
                SemesterId = semester.Id,
                LastName = NameFormatter.ToProperCase(request.LastName!),
                FirstName = NameFormatter.ToProperCase(request.FirstName!),
                MiddleName = string.IsNullOrWhiteSpace(request.MiddleName) ? string.Empty : NameFormatter.ToProperCase(request.MiddleName),
                DateOfBirth = dob,
                Birthplace = request.Birthplace!.Trim(),
                Citizenship = request.Citizenship!.Trim(),
                CivilStatus = civilStatus,
                Gender = gender,
                Email = request.Email!.Trim().ToLowerInvariant(),
                MobileNumber = request.MobileNumber!.Trim(),
                AddressLine = request.AddressLine!.Trim(),
                Barangay = request.Barangay!.Trim(),
                CityMunicipality = request.CityMunicipality!.Trim(),
                Province = request.Province!.Trim(),
                ZipCode = request.ZipCode?.Trim() ?? string.Empty,
                LastSchoolLevel = lastSchoolLevel,
                SchoolName = request.SchoolName!.Trim(),
                SchoolProgram = request.SchoolProgram?.Trim() ?? string.Empty,
                SchoolYear = request.SchoolYear?.Trim() ?? string.Empty,
                YearGradeLastAttended = yearGrade,
                LastTerm = lastTerm,
                FatherName = string.IsNullOrWhiteSpace(request.FatherName) ? string.Empty : NameFormatter.ToProperCase(request.FatherName),
                FatherMobile = request.FatherMobile?.Trim() ?? string.Empty,
                MotherName = string.IsNullOrWhiteSpace(request.MotherName) ? string.Empty : NameFormatter.ToProperCase(request.MotherName),
                MotherMobile = request.MotherMobile?.Trim() ?? string.Empty,
                GuardianRelationship = guardianRel,
                GuardianName = NameFormatter.ToProperCase(request.GuardianName!),
                GuardianMobile = request.GuardianMobile!.Trim(),
                ReferredBy = string.IsNullOrWhiteSpace(request.ReferredBy) ? null : NameFormatter.ToProperCase(request.ReferredBy),
                TermsAcceptedAtUtc = DateTime.UtcNow
            };

            // Seed the admission-requirements checklist (FR-DOC-01), initially all unsubmitted.
            foreach (var docType in Enum.GetValues<DocumentType>())
            {
                registration.Documents.Add(new RegistrationDocument { DocumentType = docType });
            }

            db.StudentRegistrations.Add(registration);
            audit.RecordAnonymous(AuditAction.StudentRegistered,
                $"Submitted a digital SIS ({Humanize(registration.StudentType.ToString())}, {registration.Program}) " +
                $"as {registration.StudentNumber}.",
                registration.FullName, "StudentRegistration", registration.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);

            // Confirmation email is best-effort: the registration is already committed.
            var (subject, body) = RegistrationEmails.RegistrationConfirmation(registration);
            var result = await email.SendAsync(registration.Email, registration.FullName, subject, body, cancellationToken);
            if (result.Sent)
            {
                audit.RecordAnonymous(AuditAction.NotificationDispatched,
                    $"Sent SIS registration confirmation to {registration.Email}.",
                    registration.FullName, "StudentRegistration", registration.Id.ToString());
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.Created($"/api/registration/{registration.Id}",
                new RegisterStudentResponse(registration.Id, registration.StudentNumber, registration.FullName, result.Sent));
        }

        /// <summary>Issues "{year}-{6-digit sequence}" for the active semester, retrying on the unique-index race.</summary>
        private static async Task<string> GenerateStudentNumberAsync(
            AppDbContext db, Semester semester, CancellationToken cancellationToken)
        {
            var year = semester.StartDate.Year;
            var prefix = $"{year}-";
            var lastForYear = await db.StudentRegistrations
                .Where(r => r.StudentNumber.StartsWith(prefix))
                .OrderByDescending(r => r.StudentNumber)
                .Select(r => r.StudentNumber)
                .FirstOrDefaultAsync(cancellationToken);

            var next = 1;
            if (lastForYear is not null && int.TryParse(lastForYear[prefix.Length..], out var parsed))
            {
                next = parsed + 1;
            }
            return $"{prefix}{next:D6}";
        }

        private static void RequireText(Dictionary<string, string[]> errors, string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors[key] = ["This field is required."];
            }
        }

        private static T ParseEnum<T>(Dictionary<string, string[]> errors, string key, string? value) where T : struct, Enum
        {
            if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse<T>(value, ignoreCase: true, out var parsed)
                && Enum.IsDefined(parsed))
            {
                return parsed;
            }
            errors[key] = ["Please choose a valid option."];
            return default;
        }

        private static string Humanize(string pascal)
        {
            var spaced = System.Text.RegularExpressions.Regex.Replace(pascal, "([a-z])([A-Z])", "$1 $2");
            return spaced.Length == 0 ? spaced : char.ToUpperInvariant(spaced[0]) + spaced[1..].ToLowerInvariant();
        }
    }
}
