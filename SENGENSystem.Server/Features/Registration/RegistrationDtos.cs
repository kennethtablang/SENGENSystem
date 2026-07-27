using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Documents;

namespace SENGENSystem.Server.Features.Registration
{
    /// <summary>Shared read models for the SIS registration slices (FR-SIS, FR-DOC).</summary>
    internal static class Iso
    {
        /// <summary>UTC timestamp as an unambiguous ISO-8601 string ("…Z") so the client can render it in PHT.</summary>
        public static string? Utc(DateTime? value) =>
            value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc).ToString("o") : null;
    }

    /// <summary>
    /// One checklist line on the Registrar's drawer. <paramref name="Statuses"/> is what this
    /// particular paper may be recorded as — the catalog decides whether a photocopy or a
    /// certificate of grades is the third option.
    /// </summary>
    public record RegistrationDocumentDto(
        string RequirementCode, string Label, string Status, IReadOnlyList<string> Statuses)
    {
        public static RegistrationDocumentDto From(RegistrationDocument d, RequirementCatalog catalog) =>
            new(d.RequirementCode,
                catalog.Label(d.RequirementCode),
                d.Status.ToString(),
                catalog.StatusesFor(d.RequirementCode).Select(s => s.ToString()).ToList());
    }

    /// <summary>A row in the Registrar's SIS management list (FR-SIS-04).</summary>
    public record RegistrationListItemDto(
        Guid Id,
        string StudentNumber,
        string FullName,
        string Program,
        string StudentType,
        string Status,
        string Email,
        string? SemesterName,
        int DocumentsSubmitted,
        int DocumentsTotal,
        string CreatedAtUtc)
    {
        public static RegistrationListItemDto From(StudentRegistration r, RequirementCatalog catalog)
        {
            var documents = DocumentChecklist.Applicable(r, catalog);
            return new(
                r.Id,
                r.StudentNumber,
                r.FullName,
                r.Program.ToString(),
                r.StudentType.ToString(),
                r.Status.ToString(),
                r.Email,
                r.Semester?.Name,
                documents.Count(d => d.Status != DocumentStatus.NotSubmitted),
                documents.Count,
                Iso.Utc(r.CreatedAtUtc)!);
        }
    }

    /// <summary>Full SIS detail for the Registrar's view/correct screen.</summary>
    public record StudentRegistrationDto(
        Guid Id,
        string StudentNumber,
        string Status,
        string StudentType,
        string Program,
        Guid SemesterId,
        string? SemesterName,
        string LastName,
        string FirstName,
        string MiddleName,
        string FullName,
        string DateOfBirth,
        string Birthplace,
        string Citizenship,
        string CivilStatus,
        string Gender,
        string Email,
        string MobileNumber,
        string AddressLine,
        string Barangay,
        string CityMunicipality,
        string Province,
        string ZipCode,
        string LastSchoolLevel,
        string SchoolName,
        string SchoolProgram,
        string SchoolYear,
        string YearGradeLastAttended,
        string LastTerm,
        string FatherName,
        string FatherMobile,
        string MotherName,
        string MotherMobile,
        string GuardianRelationship,
        string GuardianName,
        string GuardianMobile,
        string? ReferredBy,
        string? TermsAcceptedAtUtc,
        string CreatedAtUtc,
        IReadOnlyList<RegistrationDocumentDto> Documents)
    {
        public static StudentRegistrationDto From(StudentRegistration r, RequirementCatalog catalog) =>
            new(
                r.Id,
                r.StudentNumber,
                r.Status.ToString(),
                r.StudentType.ToString(),
                r.Program.ToString(),
                r.SemesterId,
                r.Semester?.Name,
                r.LastName,
                r.FirstName,
                r.MiddleName,
                r.FullName,
                r.DateOfBirth.ToString("yyyy-MM-dd"),
                r.Birthplace,
                r.Citizenship,
                r.CivilStatus.ToString(),
                r.Gender.ToString(),
                r.Email,
                r.MobileNumber,
                r.AddressLine,
                r.Barangay,
                r.CityMunicipality,
                r.Province,
                r.ZipCode,
                r.LastSchoolLevel.ToString(),
                r.SchoolName,
                r.SchoolProgram,
                r.SchoolYear,
                r.YearGradeLastAttended.ToString(),
                r.LastTerm.ToString(),
                r.FatherName,
                r.FatherMobile,
                r.MotherName,
                r.MotherMobile,
                r.GuardianRelationship.ToString(),
                r.GuardianName,
                r.GuardianMobile,
                r.ReferredBy,
                Iso.Utc(r.TermsAcceptedAtUtc),
                Iso.Utc(r.CreatedAtUtc)!,
                // Only the papers this enrollee's student type is asked for (FR-DOC-01).
                DocumentChecklist.Applicable(r, catalog)
                    .OrderBy(d => catalog.Order(d.RequirementCode))
                    .Select(d => RegistrationDocumentDto.From(d, catalog))
                    .ToList());
    }

    /// <summary>A returning-student term-activation request in the Admission Officer's queue.</summary>
    public record TermActivationDto(
        Guid Id,
        Guid StudentRegistrationId,
        // The official student number the returning student identifies themselves by. The internal
        // registration number is carried alongside it for staff who need to trace the record.
        string? OfficialStudentNumber,
        string RegistrationNumber,
        string StudentName,
        string LastName,
        string Program,
        int YearLevel,
        string YearLevelLabel,
        // What the student themselves confirmed when they filed, so the officer can see at a glance
        // where their own answer differs from the record. Null for pre-confirmation requests.
        int? DeclaredYearLevel,
        string? DeclaredYearLevelLabel,
        string? SemesterName,
        string Status,
        string RequestedAtUtc,
        string? ValidatedAtUtc,
        string? Remarks)
    {
        public static TermActivationDto From(Domain.TermActivation a) =>
            new(
                a.Id,
                a.StudentRegistrationId,
                a.StudentRegistration?.OfficialStudentNumber,
                a.StudentRegistration?.StudentNumber ?? string.Empty,
                a.StudentRegistration?.FullName ?? string.Empty,
                a.StudentRegistration?.LastName ?? string.Empty,
                a.StudentRegistration?.Program.ToString() ?? string.Empty,
                a.StudentRegistration?.YearLevel ?? 1,
                YearLevelPolicy.Label(a.StudentRegistration?.YearLevel ?? 1),
                a.DeclaredYearLevel,
                a.DeclaredYearLevel is { } declared ? YearLevelPolicy.Label(declared) : null,
                a.Semester?.Name,
                a.Status.ToString(),
                Iso.Utc(a.RequestedAtUtc)!,
                Iso.Utc(a.ValidatedAtUtc),
                a.Remarks);
    }
}
