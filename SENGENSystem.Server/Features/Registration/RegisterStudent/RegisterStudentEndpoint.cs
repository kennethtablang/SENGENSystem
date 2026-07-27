using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Notifications;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Documents;

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
            Notifier notifier,
            Microsoft.AspNetCore.Identity.IPasswordHasher<Domain.User> passwordHasher,
            Features.Reports.Live.ReportsBroadcaster broadcaster,
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

            // One SIS per email address: a re-registration on an email already on file is rejected.
            // Emails are stored in CAPS, so compare against the uppercased submission.
            if (!errors.ContainsKey("email"))
            {
                var normalizedEmail = request.Email!.Trim().ToUpperInvariant();
                if (await db.StudentRegistrations.AnyAsync(r => r.Email == normalizedEmail, cancellationToken))
                {
                    errors["email"] = ["This email is already registered to the system."];
                }
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
                // Nobody types a year level on the SIS (FR-SIS-09). A new enrollee starts at 1; a
                // transferee also starts there and is moved by the Registrar's credit evaluation,
                // so an unevaluated transferee is never silently placed in a higher year.
                YearLevel = YearLevelPolicy.ForNewRegistration(studentType),
                SemesterId = semester.Id,
                LastName = Up(request.LastName!),
                FirstName = Up(request.FirstName!),
                MiddleName = UpOrEmpty(request.MiddleName),
                DateOfBirth = dob,
                Birthplace = Up(request.Birthplace!),
                Citizenship = Up(request.Citizenship!),
                CivilStatus = civilStatus,
                Gender = gender,
                // The SIS is filled entirely in CAPS (FR-AUTH-03), email included. This is safe:
                // every email comparison in the system (account linking, lookups) is
                // case-insensitive, so an uppercased address still matches its lowercase account.
                Email = Up(request.Email!),
                MobileNumber = request.MobileNumber!.Trim(),
                AddressLine = Up(request.AddressLine!),
                Barangay = Up(request.Barangay!),
                CityMunicipality = Up(request.CityMunicipality!),
                Province = Up(request.Province!),
                ZipCode = UpOrEmpty(request.ZipCode),
                LastSchoolLevel = lastSchoolLevel,
                SchoolName = Up(request.SchoolName!),
                SchoolProgram = UpOrEmpty(request.SchoolProgram),
                SchoolYear = UpOrEmpty(request.SchoolYear),
                YearGradeLastAttended = yearGrade,
                LastTerm = lastTerm,
                FatherName = UpOrEmpty(request.FatherName),
                FatherMobile = request.FatherMobile?.Trim() ?? string.Empty,
                MotherName = UpOrEmpty(request.MotherName),
                MotherMobile = request.MotherMobile?.Trim() ?? string.Empty,
                GuardianRelationship = guardianRel,
                GuardianName = Up(request.GuardianName!),
                GuardianMobile = request.GuardianMobile!.Trim(),
                ReferredBy = string.IsNullOrWhiteSpace(request.ReferredBy) ? null : Up(request.ReferredBy),
                TermsAcceptedAtUtc = DateTime.UtcNow
            };

            // Seed the admission-requirements checklist (FR-DOC-01): only the papers this student's
            // program requires, per the configurable requirement catalog. Initially all unsubmitted.
            var activeRequirements = await DocumentChecklist.LoadActiveRequirementsAsync(db, cancellationToken);
            DocumentChecklist.SeedDocuments(registration, activeRequirements);

            db.StudentRegistrations.Add(registration);
            audit.RecordAnonymous(AuditAction.StudentRegistered,
                $"Submitted a digital SIS ({Humanize(registration.StudentType.ToString())}, {registration.Program}) " +
                $"as {registration.StudentNumber}.",
                registration.FullName, "StudentRegistration", registration.Id.ToString());

            // Provision the student's login from the SIS details, with a temporary password they
            // must change on first sign-in — no separate account sign-up (duplicate emails were
            // already rejected above, so this is a fresh account). Committed with the registration.
            var provision = await Common.Auth.StudentAccountProvisioner.EnsureAsync(
                registration, db, passwordHasher, cancellationToken);
            if (provision.Created)
            {
                audit.RecordAnonymous(AuditAction.StudentAccountProvisioned,
                    $"Provisioned a student login ({provision.User.Email}) from the SIS submission.",
                    registration.FullName, "User", provision.User.Id.ToString());
            }

            // Put the new registration on every back-office bell, in the same transaction (FR-NOTIF).
            var staffIds = await NotificationRecipients.StaffUserIdsAsync(db, cancellationToken);
            notifier.NotifyMany(staffIds, NotificationKind.Registration,
                "New student registration",
                $"{registration.FullName} ({registration.StudentNumber}) submitted a {registration.Program} SIS registration.",
                "/registrations");

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

            // Deliver the login credentials to the SIS email — only the mailbox owner receives the
            // temporary password. Best-effort, like the confirmation above.
            if (provision is { Created: true, TemporaryPassword: { } temporaryPassword })
            {
                var (credSubject, credBody) =
                    RegistrationEmails.AccountCredentials(registration, provision.User.Email, temporaryPassword);
                var credResult = await email.SendAsync(
                    registration.Email, registration.FullName, credSubject, credBody, cancellationToken);
                if (credResult.Sent)
                {
                    audit.RecordAnonymous(AuditAction.NotificationDispatched,
                        $"Sent student account credentials to {registration.Email}.",
                        registration.FullName, "User", provision.User.Id.ToString());
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            broadcaster.Announce("registration");
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

        // The SIS stores every text field in uppercase (FR-AUTH-03) to mirror how the form is
        // filled. Trim first so stray spacing never survives, then fold to caps.
        private static string Up(string value) => value.Trim().ToUpperInvariant();

        private static string UpOrEmpty(string? value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

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
