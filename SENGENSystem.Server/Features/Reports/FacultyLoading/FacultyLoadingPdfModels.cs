using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Reports.FacultyLoading
{
    /// <summary>
    /// One assignment line of the Confirmation of Faculty Loading form. The form lists one row
    /// per scheduled meeting; the subject code, title, class number, units and student count are
    /// shown once per (subject × section) group (<see cref="ShowSubjectInfo"/>) so a subject that
    /// meets twice a week doesn't repeat that information or double-count its units.
    /// </summary>
    public record FacultyLoadLineDto(
        string SubjectCode,
        string SubjectTitle,
        string ClassNo,
        string Type,          // "LEC" or "LAB"
        string Section,       // cohort, e.g. "BSCS 1-A"
        string Day,           // abbreviated per meeting, e.g. "T", "Th"
        string Time,          // 12-hour range, e.g. "8:00AM - 11:00AM"
        string Room,
        int Units,
        double ContactHours,
        int StudentCount,
        bool ShowSubjectInfo)
    {
        /// <summary>An allocated subject the scheduler never placed — flagged in the report.</summary>
        public bool IsUnscheduled => string.IsNullOrEmpty(Day);
    }

    /// <summary>Everything the form needs about one faculty member for one semester.</summary>
    public record FacultyLoadReportDto(
        string Name,
        string EmployeeId,
        string Email,
        string ProgramCode,
        int MaxLoadUnits,
        List<FacultyLoadLineDto> Lines)
    {
        /// <summary>Total teaching load in units. Counted per allocated subject, not per meeting.</summary>
        public int TotalUnits { get; init; }

        /// <summary>Total weekly contact hours actually plotted on the schedule.</summary>
        public double TotalContactHours { get; init; }

        public int SubjectCount { get; init; }

        public int ScheduledSubjectCount { get; init; }

        public int UnscheduledSubjectCount => SubjectCount - ScheduledSubjectCount;

        public bool HasUnscheduled => UnscheduledSubjectCount > 0;

        public bool IsOverloaded => TotalUnits > MaxLoadUnits;
    }

    /// <summary>
    /// The people who route and approve the confirmation memo. The system tracks an Academic Head
    /// and a School Administrator but not a separate Program Head, so the Academic Head stands in
    /// for the "THRU: Program Head" line (as on the STI form, where they are often the same person).
    /// </summary>
    public record FacultyLoadingSignatories(
        string ProgramHead,
        string AcademicHead,
        string SchoolAdmin,
        string Institution)
    {
        internal static async Task<FacultyLoadingSignatories> LoadAsync(AppDbContext db, CancellationToken ct)
        {
            static async Task<string> NameOf(AppDbContext db, UserRole role, CancellationToken ct) =>
                await db.Users.AsNoTracking()
                    .Where(u => u.Role == role && u.IsActive)
                    .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
                    .Select(u => u.FirstName + " " + u.LastName)
                    .FirstOrDefaultAsync(ct) ?? string.Empty;

            var head = await NameOf(db, UserRole.AcademicHead, ct);
            var admin = await NameOf(db, UserRole.SchoolAdmin, ct);
            return new FacultyLoadingSignatories(head, head, admin, "STI");
        }
    }

    /// <summary>
    /// Assembles the confirmation-form data. Kept separate from rendering so the same model backs
    /// both the whole-institution PDF/workbook and a single member's copy.
    /// </summary>
    internal static class FacultyLoadingPdfData
    {
        internal static async Task<List<FacultyLoadReportDto>> BuildAsync(
            Semester semester, AppDbContext db, Guid? onlyFacultyProfileId, CancellationToken ct)
        {
            var profiles = await db.FacultyProfiles.AsNoTracking()
                .Include(f => f.User)
                .Where(f => onlyFacultyProfileId == null || f.Id == onlyFacultyProfileId)
                .OrderBy(f => f.User!.LastName).ThenBy(f => f.User!.FirstName)
                .ToListAsync(ct);
            if (profiles.Count == 0) return [];

            var facultyIds = profiles.Select(p => p.Id).ToList();

            var loads = await db.FacultyLoadAssignments.AsNoTracking()
                .Where(l => l.SemesterId == semester.Id && facultyIds.Contains(l.FacultyProfileId))
                .Include(l => l.Subject)
                .Include(l => l.ClassSection)
                .ToListAsync(ct);

            // Placed meetings, joined to the allocation through the cohort (no direct FK): Section
            // carries (SubjectId, ProgramCode, YearLevel, Block); ClassSection the matching cohort.
            var meetings = (await db.ScheduleAssignments.AsNoTracking()
                .Where(a => a.SemesterId == semester.Id && facultyIds.Contains(a.FacultyProfileId))
                .Include(a => a.TimeSlot)
                .Include(a => a.Room)
                .Include(a => a.Section)
                .ToListAsync(ct))
                .Where(a => a.TimeSlot is not null && a.Section is not null)
                .ToList();

            return profiles.Select(f =>
            {
                var mine = loads.Where(l => l.FacultyProfileId == f.Id)
                    .OrderBy(l => l.Subject?.Code)
                    .ToList();

                var lines = new List<FacultyLoadLineDto>();
                var scheduledSubjects = 0;
                var totalContact = 0.0;

                foreach (var load in mine)
                {
                    var cohort = load.ClassSection;
                    var placed = meetings
                        .Where(a => a.FacultyProfileId == f.Id
                            && a.Section!.SubjectId == load.SubjectId
                            && cohort != null
                            && a.Section.ProgramCode == cohort.ProgramCode
                            && a.Section.YearLevel == cohort.YearLevel
                            && a.Section.Block == cohort.SectionName)
                        .OrderBy(a => a.TimeSlot!.Day)
                        .ThenBy(a => a.TimeSlot!.StartMinutes)
                        .ToList();

                    var type = load.Subject?.RequiresLaboratory == true ? "LAB" : "LEC";
                    var section = cohort?.DisplayName ?? string.Empty;
                    var units = load.Subject?.Units ?? 0;

                    if (placed.Count == 0)
                    {
                        // Allocated but never placed on the board — Day/Time/Room blank so the
                        // renderer can flag it instead of printing misleading values.
                        lines.Add(new FacultyLoadLineDto(
                            load.Subject?.Code ?? string.Empty,
                            load.Subject?.Title ?? string.Empty,
                            string.Empty, type, section,
                            string.Empty, string.Empty, string.Empty,
                            units, load.Subject?.Hours ?? 0, 0, ShowSubjectInfo: true));
                        continue;
                    }

                    scheduledSubjects++;
                    // One row per meeting; subject code/title/class-no/units/students appear only
                    // on the first row of the group so the columns foot to the totals.
                    var first = true;
                    foreach (var a in placed)
                    {
                        var contact = (a.TimeSlot!.EndMinutes - a.TimeSlot.StartMinutes) / 60.0;
                        totalContact += contact;
                        var students = a.Section!.EnrolledCount > 0 ? a.Section.EnrolledCount : a.Section.Capacity;
                        lines.Add(new FacultyLoadLineDto(
                            load.Subject?.Code ?? string.Empty,
                            load.Subject?.Title ?? string.Empty,
                            a.Section.SectionCode,
                            type, section,
                            DayAbbr(a.TimeSlot.Day),
                            $"{H12(a.TimeSlot.StartMinutes)} - {H12(a.TimeSlot.EndMinutes)}",
                            a.Room?.Name ?? string.Empty,
                            units, contact, students, ShowSubjectInfo: first));
                        first = false;
                    }
                }

                return new FacultyLoadReportDto(
                    f.User?.FullName ?? "(unknown)",
                    string.IsNullOrWhiteSpace(f.EmployeeId) ? "—" : f.EmployeeId,
                    f.User?.Email ?? "—",
                    f.ProgramCode,
                    f.MaxLoadUnits,
                    lines)
                {
                    TotalUnits = mine.Sum(l => l.Subject?.Units ?? 0),
                    TotalContactHours = totalContact,
                    SubjectCount = mine.Count,
                    ScheduledSubjectCount = scheduledSubjects
                };
            }).ToList();
        }

        /// <summary>STI-style single/two-letter weekday abbreviation (M, T, W, Th, F, S).</summary>
        internal static string DayAbbr(DayOfWeek day) => day switch
        {
            DayOfWeek.Monday => "M",
            DayOfWeek.Tuesday => "T",
            DayOfWeek.Wednesday => "W",
            DayOfWeek.Thursday => "Th",
            DayOfWeek.Friday => "F",
            DayOfWeek.Saturday => "S",
            _ => "Su"
        };

        /// <summary>12-hour clock, no leading zero, e.g. 480 → "8:00AM", 780 → "1:00PM".</summary>
        internal static string H12(int minutes)
        {
            var h = minutes / 60;
            var m = minutes % 60;
            var suffix = h < 12 ? "AM" : "PM";
            var h12 = h % 12 == 0 ? 12 : h % 12;
            return $"{h12}:{m:00}{suffix}";
        }
    }
}
