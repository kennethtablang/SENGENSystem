using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.AcademicSetup
{
    /// <summary>
    /// Read models for the School Admin's academic-setup screens (school years, semesters,
    /// buildings, rooms). Dates are projected as unambiguous ISO strings so the client renders
    /// them consistently; parent names are inlined so lists read without extra lookups.
    /// </summary>
    internal static class IsoDate
    {
        /// <summary>A calendar date as "yyyy-MM-dd" (round-trippable by an &lt;input type="date"&gt;).</summary>
        public static string Of(DateOnly value) => value.ToString("yyyy-MM-dd");

        /// <summary>Parses an ISO "yyyy-MM-dd" date from the client; null/blank/malformed yields null.</summary>
        public static DateOnly? TryParse(string? value) =>
            DateOnly.TryParse(value, out var d) ? d : null;
    }

    public record SchoolYearDto(
        Guid Id,
        string Name,
        bool IsActive,
        string StartDate,
        string EndDate,
        int SemesterCount)
    {
        public static SchoolYearDto From(SchoolYear y, int semesterCount) =>
            new(y.Id, y.Name, y.IsActive, IsoDate.Of(y.StartDate), IsoDate.Of(y.EndDate), semesterCount);
    }

    public record SemesterDto(
        Guid Id,
        string Name,
        bool IsActive,
        string StartDate,
        string EndDate,
        Guid? SchoolYearId,
        string? SchoolYearName)
    {
        public static SemesterDto From(Semester s) =>
            new(s.Id, s.Name, s.IsActive, IsoDate.Of(s.StartDate), IsoDate.Of(s.EndDate),
                s.SchoolYearId, s.SchoolYear?.Name);
    }

    public record BuildingDto(
        Guid Id,
        string Name,
        string? Code,
        int RoomCount)
    {
        public static BuildingDto From(Building b, int roomCount) =>
            new(b.Id, b.Name, b.Code, roomCount);
    }

    public record RoomDto(
        Guid Id,
        string Name,
        int Capacity,
        bool IsLaboratory,
        Guid? BuildingId,
        string? BuildingName)
    {
        public static RoomDto From(Room r) =>
            new(r.Id, r.Name, r.Capacity, r.IsLaboratory, r.BuildingId, r.Building?.Name);
    }
}
