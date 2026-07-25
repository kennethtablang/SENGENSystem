using System.Net.Mail;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Common.Validation;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Documents;

namespace SENGENSystem.Server.Features.PreEnrollment.Import
{
    // The ETL pipeline behind FR-PRE-01: Extract rows from .xlsx (ClosedXML) → Validate field
    // completeness and format consistency → Transform to StudentRegistration (proper-cased
    // names, parsed enums/dates, issued student numbers) → Load with duplicate detection.
    // Errors are reported row-by-row and never abort the valid rows (FR-PRE-03).
    public record ImportRowResult(int Row, string? StudentNumber, string Name, string Outcome, IReadOnlyList<string> Errors);

    public record ImportReport(int TotalRows, int Loaded, int Skipped, int Failed, IReadOnlyList<ImportRowResult> Rows);

    internal static class XlsxStudentImporter
    {
        public const string OutcomeLoaded = "Loaded";
        public const string OutcomeSkipped = "Skipped";   // duplicate — already known
        public const string OutcomeFailed = "Failed";     // validation errors

        /// <summary>Template header row; matching is case- and space-insensitive.</summary>
        public static readonly string[] Columns =
        [
            "StudentType", "Program", "LastName", "FirstName", "MiddleName", "DateOfBirth",
            "Gender", "CivilStatus", "Email", "MobileNumber",
            "AddressLine", "Barangay", "CityMunicipality", "Province", "ZipCode",
            "LastSchoolLevel", "SchoolName", "YearGradeLastAttended", "LastTerm",
            "GuardianRelationship", "GuardianName", "GuardianMobile"
        ];

        public static async Task<ImportReport> ImportAsync(
            Stream xlsx, AppDbContext db, Semester semester, CancellationToken cancellationToken)
        {
            using var workbook = new XLWorkbook(xlsx);
            var sheet = workbook.Worksheets.First();

            var headerMap = MapHeaders(sheet);
            if (!headerMap.ContainsKey("lastname") || !headerMap.ContainsKey("firstname") || !headerMap.ContainsKey("email"))
            {
                throw new FormatException(
                    "The workbook's first row must be a header row containing at least LastName, FirstName, and Email. " +
                    "Download the template for the expected layout.");
            }

            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
            var results = new List<ImportRowResult>();

            // Duplicate detection state: existing records once from the DB, plus what this file loads.
            var knownEmails = new HashSet<string>(
                await db.StudentRegistrations.Select(r => r.Email).ToListAsync(cancellationToken),
                StringComparer.OrdinalIgnoreCase);
            var knownIdentities = new HashSet<string>(
                (await db.StudentRegistrations
                    .Select(r => new { r.LastName, r.FirstName, r.DateOfBirth })
                    .ToListAsync(cancellationToken))
                .Select(x => IdentityKey(x.LastName, x.FirstName, x.DateOfBirth)),
                StringComparer.OrdinalIgnoreCase);

            var nextSequence = await NextSequenceAsync(db, semester, cancellationToken);
            var prefix = $"{semester.StartDate.Year}-";

            // Active requirement catalog, loaded once — each row seeds only the papers its program needs.
            var activeRequirements = await DocumentChecklist.LoadActiveRequirementsAsync(db, cancellationToken);

            for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
            {
                var row = sheet.Row(rowNumber);
                if (row.IsEmpty()) continue;

                string Cell(string column) =>
                    headerMap.TryGetValue(column, out var col) ? row.Cell(col).GetString().Trim() : string.Empty;

                var errors = new List<string>();
                var lastName = Cell("lastname");
                var firstName = Cell("firstname");
                var email = Cell("email").ToLowerInvariant();
                var displayName = $"{firstName} {lastName}".Trim();

                // ---- Validate: required fields ----
                Require(errors, "LastName", lastName);
                Require(errors, "FirstName", firstName);
                Require(errors, "MobileNumber", Cell("mobilenumber"));
                if (email.Length == 0 || !MailAddress.TryCreate(email, out _))
                {
                    errors.Add("Email is missing or not a valid address.");
                }

                // ---- Validate: enums (blank optional ones fall back to sensible defaults) ----
                var studentType = ParseEnum<StudentType>(errors, "StudentType", Cell("studenttype"), required: true);
                var program = ParseEnum<ProgramTrack>(errors, "Program", Cell("program"), required: true);
                var gender = ParseEnum<Gender>(errors, "Gender", Cell("gender"), required: true);
                var civilStatus = ParseEnum(errors, "CivilStatus", Cell("civilstatus"), CivilStatus.Single);
                var schoolLevel = ParseEnum(errors, "LastSchoolLevel", Cell("lastschoollevel"), LastSchoolLevel.SeniorHighSchool);
                var yearGrade = ParseEnum(errors, "YearGradeLastAttended", Cell("yeargradelastattended"), YearGradeLevel.Grade12);
                var lastTerm = ParseEnum(errors, "LastTerm", Cell("lastterm"), AcademicTerm.Second);
                var guardianRel = ParseEnum(errors, "GuardianRelationship", Cell("guardianrelationship"), GuardianRelationship.Other);

                // ---- Validate: date of birth (date-typed cell or ISO/short date text) ----
                DateOnly dob = default;
                var dobCell = headerMap.TryGetValue("dateofbirth", out var dobCol) ? row.Cell(dobCol) : null;
                if (dobCell is not null && dobCell.TryGetValue<DateTime>(out var dobDate))
                {
                    dob = DateOnly.FromDateTime(dobDate);
                }
                else if (!DateOnly.TryParse(Cell("dateofbirth"), out dob))
                {
                    errors.Add("DateOfBirth is missing or not a recognizable date (use YYYY-MM-DD).");
                }

                if (errors.Count > 0)
                {
                    results.Add(new ImportRowResult(rowNumber, null, displayName, OutcomeFailed, errors));
                    continue;
                }

                // ---- Load: duplicate detection against the DB and within this file ----
                var identity = IdentityKey(lastName, firstName, dob);
                if (knownEmails.Contains(email))
                {
                    results.Add(new ImportRowResult(rowNumber, null, displayName, OutcomeSkipped,
                        [$"A registration with email {email} already exists."]));
                    continue;
                }
                if (knownIdentities.Contains(identity))
                {
                    results.Add(new ImportRowResult(rowNumber, null, displayName, OutcomeSkipped,
                        ["A registration with the same name and date of birth already exists."]));
                    continue;
                }
                knownEmails.Add(email);
                knownIdentities.Add(identity);

                // ---- Transform → target schema ----
                var registration = new StudentRegistration
                {
                    StudentNumber = $"{prefix}{nextSequence++:D6}",
                    Status = RegistrationStatus.Submitted,
                    StudentType = studentType,
                    Program = program,
                    SemesterId = semester.Id,
                    LastName = NameFormatter.ToProperCase(lastName),
                    FirstName = NameFormatter.ToProperCase(firstName),
                    MiddleName = Proper(Cell("middlename")),
                    DateOfBirth = dob,
                    Birthplace = string.Empty,
                    Citizenship = "Filipino",
                    CivilStatus = civilStatus,
                    Gender = gender,
                    Email = email,
                    MobileNumber = Cell("mobilenumber"),
                    AddressLine = Cell("addressline"),
                    Barangay = Cell("barangay"),
                    CityMunicipality = Cell("citymunicipality"),
                    Province = Cell("province"),
                    ZipCode = Cell("zipcode"),
                    LastSchoolLevel = schoolLevel,
                    SchoolName = Cell("schoolname"),
                    SchoolProgram = string.Empty,
                    SchoolYear = string.Empty,
                    YearGradeLastAttended = yearGrade,
                    LastTerm = lastTerm,
                    GuardianRelationship = guardianRel,
                    GuardianName = Proper(Cell("guardianname")),
                    GuardianMobile = Cell("guardianmobile")
                };
                DocumentChecklist.SeedDocuments(registration, activeRequirements);
                db.StudentRegistrations.Add(registration);

                results.Add(new ImportRowResult(
                    rowNumber, registration.StudentNumber, registration.FullName, OutcomeLoaded, []));
            }

            return new ImportReport(
                results.Count,
                results.Count(r => r.Outcome == OutcomeLoaded),
                results.Count(r => r.Outcome == OutcomeSkipped),
                results.Count(r => r.Outcome == OutcomeFailed),
                results);
        }

        /// <summary>Builds the downloadable .xlsx template with headers and one example row.</summary>
        public static byte[] BuildTemplate()
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.AddWorksheet("Prospective students");
            for (var i = 0; i < Columns.Length; i++)
            {
                sheet.Cell(1, i + 1).Value = Columns[i];
                sheet.Cell(1, i + 1).Style.Font.Bold = true;
            }
            string[] example =
            [
                "NewStudent", "ITP", "Dela Cruz", "Juan", "Santos", "2007-06-15",
                "Male", "Single", "juan.delacruz@example.com", "09171234567",
                "123 Rizal St", "Poblacion", "Alaminos", "Pangasinan", "2404",
                "SeniorHighSchool", "Alaminos NHS", "Grade12", "Second",
                "Mother", "Maria Dela Cruz", "09170000000"
            ];
            for (var i = 0; i < example.Length; i++)
            {
                sheet.Cell(2, i + 1).Value = example[i];
            }
            sheet.Columns().AdjustToContents();
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static Dictionary<string, int> MapHeaders(IXLWorksheet sheet)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var header = sheet.Row(1);
            var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (var col = 1; col <= lastColumn; col++)
            {
                var name = header.Cell(col).GetString().Trim().Replace(" ", "").ToLowerInvariant();
                if (name.Length > 0)
                {
                    map[name] = col;
                }
            }
            return map;
        }

        private static async Task<int> NextSequenceAsync(
            AppDbContext db, Semester semester, CancellationToken cancellationToken)
        {
            var prefix = $"{semester.StartDate.Year}-";
            var last = await db.StudentRegistrations
                .Where(r => r.StudentNumber.StartsWith(prefix))
                .OrderByDescending(r => r.StudentNumber)
                .Select(r => r.StudentNumber)
                .FirstOrDefaultAsync(cancellationToken);
            return last is not null && int.TryParse(last[prefix.Length..], out var parsed) ? parsed + 1 : 1;
        }

        private static string IdentityKey(string lastName, string firstName, DateOnly dob) =>
            $"{lastName.Trim()}|{firstName.Trim()}|{dob:yyyy-MM-dd}";

        private static void Require(List<string> errors, string field, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{field} is required.");
            }
        }

        private static T ParseEnum<T>(List<string> errors, string field, string value, bool required) where T : struct, Enum
        {
            if (Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            {
                return parsed;
            }
            errors.Add(string.IsNullOrWhiteSpace(value)
                ? $"{field} is required (one of: {string.Join(", ", Enum.GetNames<T>())})."
                : $"{field} value \"{value}\" is not recognized (one of: {string.Join(", ", Enum.GetNames<T>())}).");
            return default;
        }

        private static T ParseEnum<T>(List<string> errors, string field, string value, T fallback) where T : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }
            if (Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            {
                return parsed;
            }
            errors.Add($"{field} value \"{value}\" is not recognized (one of: {string.Join(", ", Enum.GetNames<T>())}).");
            return fallback;
        }

        private static string Proper(string value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : NameFormatter.ToProperCase(value);
    }
}
