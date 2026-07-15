using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Registration
{
    /// <summary>Shared read models for the SIS registration slices (FR-SIS, FR-DOC).</summary>
    internal static class Iso
    {
        /// <summary>UTC timestamp as an unambiguous ISO-8601 string ("…Z") so the client can render it in PHT.</summary>
        public static string? Utc(DateTime? value) =>
            value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc).ToString("o") : null;
    }

    public record RegistrationDocumentDto(string DocumentType, string Status)
    {
        public static RegistrationDocumentDto From(RegistrationDocument d) =>
            new(d.DocumentType.ToString(), d.Status.ToString());
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
        public static RegistrationListItemDto From(StudentRegistration r) =>
            new(
                r.Id,
                r.StudentNumber,
                r.FullName,
                r.Program.ToString(),
                r.StudentType.ToString(),
                r.Status.ToString(),
                r.Email,
                r.Semester?.Name,
                r.Documents.Count(d => d.Status != DocumentStatus.NotSubmitted),
                r.Documents.Count,
                Iso.Utc(r.CreatedAtUtc)!);
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
        public static StudentRegistrationDto From(StudentRegistration r) =>
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
                r.Documents
                    .OrderBy(d => d.DocumentType)
                    .Select(RegistrationDocumentDto.From)
                    .ToList());
    }

    /// <summary>A returning-student term-activation request in the Admission Officer's queue.</summary>
    public record TermActivationDto(
        Guid Id,
        Guid StudentRegistrationId,
        string StudentNumber,
        string StudentName,
        string Program,
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
                a.StudentRegistration?.StudentNumber ?? string.Empty,
                a.StudentRegistration?.FullName ?? string.Empty,
                a.StudentRegistration?.Program.ToString() ?? string.Empty,
                a.Semester?.Name,
                a.Status.ToString(),
                Iso.Utc(a.RequestedAtUtc)!,
                Iso.Utc(a.ValidatedAtUtc),
                a.Remarks);
    }
}
