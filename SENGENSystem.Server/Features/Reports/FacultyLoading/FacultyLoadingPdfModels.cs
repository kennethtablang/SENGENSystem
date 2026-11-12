using SENGENSystem.Server.Common.Formatting;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Scheduling;

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
        string ClassNo,       // Section.ClassNumber — minted by the database, stable for the term
        string Type,          // "LEC" or "LAB" — per meeting, from the room it is placed in
        string Section,       // cohort, e.g. "BSCS 1-A"
        string Day,           // abbreviated per meeting, e.g. "T", "Th"
        string Time,          // 12-hour range, e.g. "8:00AM - 11:00AM"
        string Room,
        int Units,
        double ContactHours,
        double TeachingHours, // contact hours as credited: laboratory × SystemSettings.LabTeachingCreditFactor
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

        /// <summary>
        /// Total weekly teaching hours credited for the plotted meetings — the contact hours with
        /// laboratory counted at the institution's credit factor. Equal to contact hours when the
        /// factor is 1.0 (the default).
        /// </summary>
        public double TotalTeachingHours { get; init; }

        public int SubjectCount { get; init; }

        public int ScheduledSubjectCount { get; init; }

        public int UnscheduledSubjectCount => SubjectCount - ScheduledSubjectCount;

        public bool HasUnscheduled => UnscheduledSubjectCount > 0;

        public bool IsOverloaded => TotalUnits > MaxLoadUnits;
    }

    /// <summary>
    /// The people who route and approve the confirmation memo. The "THRU: Program Head" line names
    /// the account designated head of the faculty member's program (<see cref="User.ProgramHeadOf"/>,
    /// set on System parameters); a program with no designated head falls back to the Academic
    /// Head, as on the STI form where they are often the same person.
    /// </summary>
    public record FacultyLoadingSignatories(
        IReadOnlyDictionary<string, string> ProgramHeads,
        string AcademicHead,
        string SchoolAdmin,
        string Institution)
    {
        /// <summary>Who signs THRU for a member of <paramref name="programCode"/>.</summary>
        public string ProgramHeadFor(string? programCode) =>
            programCode is not null && ProgramHeads.TryGetValue(programCode, out var head) ? head : AcademicHead;

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
            var programHeads = (await db.Users.AsNoTracking()
                    .Where(u => u.IsActive && u.ProgramHeadOf != null)
                    .Select(u => new { Program = u.ProgramHeadOf!, Name = u.FirstName + " " + u.LastName })
                    .ToListAsync(ct))
                .GroupBy(x => x.Program, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
            var institution = (await db.GetSettingsAsync(ct)).InstitutionName;
            return new FacultyLoadingSignatories(programHeads, head, admin, institution.ToUpperInvariant());
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

            var labFactor = (await db.GetSettingsAsync(ct)).LabTeachingCreditFactor;
            var facultyIds = profiles.Select(p => p.Id).ToList();

            // Each member's allocations, grouped once rather than re-filtered per member.
            var loadsByFaculty = (await db.FacultyLoadAssignments.AsNoTracking()
                .Where(l => l.SemesterId == semester.Id && facultyIds.Contains(l.FacultyProfileId))
                .Include(l => l.Subject)
                .Include(l => l.ClassSection)
                .ToListAsync(ct))
                .ToLookup(l => l.FacultyProfileId);

            // Placed meetings, joined to the allocation through the cohort (no direct FK): Section
            // carries (SubjectId, ProgramCode, YearLevel, Block); ClassSection the matching cohort.
            // Filtered in SQL, then indexed once by that key — each allocation finds its meetings
            // by lookup instead of rescanning every meeting of the term.
            var meetings = (await db.ScheduleAssignments.AsNoTracking()
                .Where(a => a.SemesterId == semester.Id && facultyIds.Contains(a.FacultyProfileId)
                    && a.TimeSlot != null && a.Section != null)
                .Include(a => a.TimeSlot)
                .Include(a => a.Room)
                .Include(a => a.Section)
                .ToListAsync(ct))
                .ToLookup(a => MeetingKey(a.FacultyProfileId, a.Section!.SubjectId,
                    a.Section.ProgramCode, a.Section.YearLevel, a.Section.Block));

            return profiles.Select(f =>
            {
                var mine = loadsByFaculty[f.Id]
                    .OrderBy(l => l.Subject?.Code)
                    .ToList();

                var lines = new List<FacultyLoadLineDto>();
                var scheduledSubjects = 0;
                var totalContact = 0.0;
                var totalTeaching = 0.0;

                foreach (var load in mine)
                {
                    var cohort = load.ClassSection;
                    var placed = (cohort is null
                            ? Enumerable.Empty<ScheduleAssignment>()
                            : meetings[MeetingKey(f.Id, load.SubjectId, cohort.ProgramCode, cohort.YearLevel, cohort.SectionName)])
                        .OrderBy(a => a.TimeSlot!.Day)
                        .ThenBy(a => a.TimeSlot!.StartMinutes)
                        .ToList();

                    var section = cohort?.DisplayName ?? string.Empty;
                    var units = load.Subject?.Units ?? 0;

                    if (placed.Count == 0)
                    {
                        // Allocated but never placed on the board — Day/Time/Room blank so the
                        // renderer can flag it instead of printing misleading values. The hours
                        // shown are what the subject is planned to need.
                        var type = load.Subject?.RequiresLaboratory == true ? "LAB" : "LEC";
                        var plannedTeaching = (load.Subject?.LectureHours ?? 0)
                            + (load.Subject?.LaboratoryHours ?? 0) * labFactor;
                        lines.Add(new FacultyLoadLineDto(
                            load.Subject?.Code ?? string.Empty,
                            load.Subject?.Title ?? string.Empty,
                            string.Empty, type, section,
                            string.Empty, string.Empty, string.Empty,
                            units, load.Subject?.Hours ?? 0, plannedTeaching, 0, ShowSubjectInfo: true));
                        continue;
                    }

                    scheduledSubjects++;
                    // One row per meeting; subject code/title/class-no/units/students appear only
                    // on the first row of the group so the columns foot to the totals.
                    var first = true;
                    foreach (var a in placed)
                    {
                        var contact = (a.TimeSlot!.EndMinutes - a.TimeSlot.StartMinutes) / 60.0;
                        // A meeting's component follows its room, as on the board: a laboratory
                        // meeting is credited at the institution's factor, a lecture hour for hour.
                        var isLab = ScheduleRowDto.ComponentOf(a) == ClassComponent.Laboratory;
                        var teaching = isLab ? contact * labFactor : contact;
                        totalContact += contact;
                        totalTeaching += teaching;
                        var students = a.Section!.EnrolledCount > 0 ? a.Section.EnrolledCount : a.Section.Capacity;
                        lines.Add(new FacultyLoadLineDto(
                            load.Subject?.Code ?? string.Empty,
                            load.Subject?.Title ?? string.Empty,
                            a.Section.ClassNumber.ToString(),
                            isLab ? "LAB" : "LEC", section,
                            ClockText.DayAbbr(a.TimeSlot.Day),
                            $"{ClockText.H12(a.TimeSlot.StartMinutes)} - {ClockText.H12(a.TimeSlot.EndMinutes)}",
                            a.Room?.Name ?? string.Empty,
                            units, contact, teaching, students, ShowSubjectInfo: first));
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
                    TotalTeachingHours = totalTeaching,
                    SubjectCount = mine.Count,
                    ScheduledSubjectCount = scheduledSubjects
                };
            }).ToList();
        }

        private static string MeetingKey(Guid facultyId, Guid subjectId, string programCode, int yearLevel, string block) =>
            $"{facultyId:N}|{subjectId:N}|{programCode.ToUpperInvariant()}|{yearLevel}|{block.ToUpperInvariant()}";
    }
}
