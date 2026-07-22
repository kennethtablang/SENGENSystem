using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Auditing;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Reports.SystemExport
{
    // Vertical slice: the system-parameters counterpart of the semester export bundle
    // (FR-RPT-02). One .xlsx workbook capturing the setup/master data the whole system
    // runs on — academic calendar, buildings & rooms, allowable time slots, curricula &
    // subjects, class sections, faculty profiles, and user accounts — so the configuration
    // can be filed away or reviewed offline. Admin-only: it includes every account's
    // email and role.
    public static class SystemParametersExportEndpoint
    {
        private const string Scope = "system parameters";

        public static IEndpointRouteBuilder MapSystemParametersExport(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/reports/system-export", HandleAsync)
                .RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.SchoolAdmin)));
            return app;
        }

        private static async Task<IResult> HandleAsync(AppDbContext db, AuditLog audit, CancellationToken ct)
        {
            var schoolYears = await db.SchoolYears.AsNoTracking()
                .Include(y => y.Semesters)
                .OrderBy(y => y.StartDate)
                .ToListAsync(ct);
            var buildings = await db.Buildings.AsNoTracking()
                .Include(b => b.Rooms)
                .OrderBy(b => b.Name)
                .ToListAsync(ct);
            var timeSlots = await db.TimeSlots.AsNoTracking()
                .OrderBy(t => t.Day).ThenBy(t => t.StartMinutes)
                .ToListAsync(ct);
            var curricula = await db.Curricula.AsNoTracking()
                .Include(c => c.Subjects)
                .Include(c => c.SchoolYears).ThenInclude(link => link.SchoolYear)
                .OrderBy(c => c.ProgramCode)
                .ToListAsync(ct);
            var prerequisites = await db.SubjectPrerequisites.AsNoTracking()
                .Include(p => p.PrerequisiteSubject)
                .ToListAsync(ct);
            var classSections = await db.ClassSections.AsNoTracking()
                .Include(c => c.Semester)
                .OrderBy(c => c.ProgramCode).ThenBy(c => c.YearLevel).ThenBy(c => c.SectionName)
                .ToListAsync(ct);
            var faculty = await db.FacultyProfiles.AsNoTracking()
                .Include(f => f.User)
                .OrderBy(f => f.User!.LastName).ThenBy(f => f.User!.FirstName)
                .ToListAsync(ct);
            var preferences = (await db.FacultyTimePreferences.AsNoTracking().ToListAsync(ct))
                .GroupBy(p => p.FacultyProfileId)
                .ToDictionary(g => g.Key, g => g
                    .OrderBy(p => p.Day).ThenBy(p => p.StartMinutes)
                    .Select(p => $"{p.Day} {Hhmm(p.StartMinutes)}–{Hhmm(p.EndMinutes)}"));
            var users = await db.Users.AsNoTracking()
                .OrderBy(u => u.Role).ThenBy(u => u.LastName).ThenBy(u => u.FirstName)
                .ToListAsync(ct);

            using var workbook = new XLWorkbook();

            WriteOverviewSheet(workbook, schoolYears, buildings, timeSlots, curricula, classSections, faculty, users);

            // Academic calendar: every semester under its school year (years without terms still listed).
            var calendarRows = schoolYears
                .SelectMany(y => y.Semesters.OrderBy(s => s.StartDate).DefaultIfEmpty(), (y, s) => new object[]
                {
                    y.Name, y.IsActive ? "Active" : "—", $"{y.StartDate:yyyy-MM-dd} to {y.EndDate:yyyy-MM-dd}",
                    s?.Name ?? "(no semesters yet)",
                    s is null ? "" : $"{s.StartDate:yyyy-MM-dd} to {s.EndDate:yyyy-MM-dd}",
                    s is null ? "" : s.IsActive ? "Active" : s.IsArchived ? "Archived" : "Inactive"
                })
                .ToList();
            ReportsEndpoints.WriteTableSheet(workbook, "Academic calendar", "School years & semesters", Scope,
                ["School year", "SY status", "SY dates", "Semester", "Term dates", "Status"], calendarRows);

            var roomRows = buildings
                .SelectMany(b => b.Rooms.OrderBy(r => r.Name).DefaultIfEmpty(), (b, r) => new object[]
                {
                    b.Name, b.Code ?? "—",
                    r?.Name ?? "(no rooms yet)",
                    r?.Capacity ?? 0,
                    r is null ? "" : r.Kind.Label()
                })
                .ToList();
            ReportsEndpoints.WriteTableSheet(workbook, "Buildings & rooms", "Buildings & rooms", Scope,
                ["Building", "Code", "Room", "Capacity", "Type"], roomRows);

            var slotRows = timeSlots
                .Select(t => new object[]
                {
                    t.Day.ToString(), Hhmm(t.StartMinutes), Hhmm(t.EndMinutes),
                    t.EndMinutes - t.StartMinutes
                })
                .ToList();
            ReportsEndpoints.WriteTableSheet(workbook, "Time slots", "Allowable time slots", Scope,
                ["Day", "Start", "End", "Minutes"], slotRows);

            var curriculumRows = curricula
                .Select(c => new object[]
                {
                    c.ProgramCode, c.ProgramName, c.IsActive ? "Active" : "Inactive",
                    c.Subjects.Count(s => !s.IsArchived), c.Subjects.Count(s => s.IsArchived),
                    string.Join(", ", c.SchoolYears
                        .Select(link => link.SchoolYear?.Name)
                        .Where(n => n is not null))
                })
                .ToList();
            ReportsEndpoints.WriteTableSheet(workbook, "Curricula", "Program curricula", Scope,
                ["Program", "Name", "Status", "Subjects", "Archived subjects", "Effective school years"], curriculumRows);

            var prereqBySubject = prerequisites
                .GroupBy(p => p.SubjectId)
                .ToDictionary(g => g.Key, g => string.Join(", ", g
                    .Select(p => p.PrerequisiteSubject?.Code)
                    .Where(c => c is not null)
                    .OrderBy(c => c)));
            var subjectRows = curricula
                .SelectMany(c => c.Subjects
                    .OrderBy(s => s.YearLevel).ThenBy(s => s.Term).ThenBy(s => s.Code)
                    .Select(s => new object[]
                    {
                        c.ProgramCode, s.Code, s.Title, s.Units, s.Hours,
                        s.YearLevel, s.Term == SemesterTerm.SecondSemester ? "2nd sem" : "1st sem",
                        s.Delivery.Label(),
                        s.Delivery == SubjectDelivery.LectureLaboratory
                            ? $"{s.LectureHours}h lec + {s.LaboratoryHours}h lab"
                            : "—",
                        s.LabRoomKind?.Label() ?? "—",
                        prereqBySubject.GetValueOrDefault(s.Id, ""),
                        s.IsArchived
                            ? $"Archived{(s.ArchiveReason is null ? "" : $" — {s.ArchiveReason}")}"
                            : "Active"
                    }))
                .ToList();
            ReportsEndpoints.WriteTableSheet(workbook, "Subjects", "Curriculum subjects", Scope,
                ["Program", "Code", "Title", "Units", "Hrs/week", "Year", "Term", "Delivery",
                 "Hour split", "Laboratory needed", "Prerequisites", "Status"], subjectRows);

            var classRows = classSections
                .Select(c => new object[]
                {
                    c.Semester?.Name ?? "—", c.ProgramCode, c.YearLevel, c.SectionName, c.DisplayName
                })
                .ToList();
            ReportsEndpoints.WriteTableSheet(workbook, "Class sections", "Class sections (student blocks)", Scope,
                ["Semester", "Program", "Year", "Section", "Block"], classRows);

            var facultyRows = faculty
                .Select(f => new object[]
                {
                    f.User?.FullName ?? "(unknown)", f.EmployeeId, f.ProgramCode, f.MaxLoadUnits,
                    f.User?.IsActive == false ? "Deactivated" : "Active",
                    string.Join("; ", preferences.GetValueOrDefault(f.Id, []))
                })
                .ToList();
            ReportsEndpoints.WriteTableSheet(workbook, "Faculty", "Faculty profiles", Scope,
                ["Faculty", "Employee ID", "Program", "Load ceiling (units)", "Account", "Preferred windows"], facultyRows);

            var userRows = users
                .Select(u => new object[]
                {
                    u.FullName, u.Email, u.Role.ToString(),
                    u.IsActive ? "Active" : "Deactivated",
                    $"{u.CreatedAtUtc:yyyy-MM-dd}"
                })
                .ToList();
            ReportsEndpoints.WriteTableSheet(workbook, "User accounts", "User accounts", Scope,
                ["Name", "Email", "Role", "Status", "Created (UTC)"], userRows);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            // Same reasoning as the semester bundle: a full configuration dump — including
            // every account's email — belongs in the audit trail (FR-AUD-01).
            audit.Record(AuditAction.SystemParametersExported,
                $"Exported the system parameters workbook: {schoolYears.Count} school year(s), " +
                $"{buildings.Sum(b => b.Rooms.Count)} room(s), {timeSlots.Count} time slot(s), " +
                $"{curricula.Count} curriculum(s), {users.Count} user account(s).");
            await db.SaveChangesAsync(ct);

            return Results.File(stream.ToArray(), ReportsEndpoints.XlsxContentType,
                "sengen-system-parameters.xlsx");
        }

        private static void WriteOverviewSheet(
            XLWorkbook workbook,
            List<SchoolYear> schoolYears, List<Building> buildings, List<TimeSlot> timeSlots,
            List<Domain.Curriculum> curricula, List<ClassSection> classSections,
            List<FacultyProfile> faculty, List<User> users)
        {
            var sheet = workbook.AddWorksheet("Overview");
            sheet.Cell(1, 1).Value = "SEN-GEN system parameters export";
            sheet.Cell(1, 1).Style.Font.Bold = true;

            var rooms = buildings.SelectMany(b => b.Rooms).ToList();
            var subjects = curricula.SelectMany(c => c.Subjects).ToList();
            (string Label, XLCellValue Value)[] facts =
            [
                ("School years", schoolYears.Count),
                ("Semesters", schoolYears.Sum(y => y.Semesters.Count)),
                ("Buildings", buildings.Count),
                // Broken out by kind: the two laboratories are the scarce resource the whole
                // timetable contends for, so "how many labs" alone doesn't say enough.
                ("Rooms (lecture / computer lab / kitchen lab)",
                    $"{rooms.Count(r => r.Kind == RoomKind.LectureRoom)} / " +
                    $"{rooms.Count(r => r.Kind == RoomKind.ComputerLaboratory)} / " +
                    $"{rooms.Count(r => r.Kind == RoomKind.KitchenLaboratory)}"),
                ("Allowable time slots", timeSlots.Count),
                ("Curricula", curricula.Count),
                ("Subjects (active / archived)", $"{subjects.Count(s => !s.IsArchived)} / {subjects.Count(s => s.IsArchived)}"),
                ("Class sections (blocks)", classSections.Count),
                ("Faculty profiles", faculty.Count),
                ("User accounts (active / deactivated)", $"{users.Count(u => u.IsActive)} / {users.Count(u => !u.IsActive)}"),
                ("Exported (UTC)", $"{DateTime.UtcNow:yyyy-MM-dd HH:mm}")
            ];
            for (var i = 0; i < facts.Length; i++)
            {
                sheet.Cell(3 + i, 1).Value = facts[i].Label;
                sheet.Cell(3 + i, 1).Style.Font.Bold = true;
                sheet.Cell(3 + i, 2).Value = facts[i].Value;
            }

            sheet.Cell(3 + facts.Length + 1, 1).Value =
                "Sheets: Academic calendar · Buildings & rooms · Time slots · Curricula · Subjects · Class sections · Faculty · User accounts";
            sheet.Columns().AdjustToContents();
        }

        private static string Hhmm(int minutes) => $"{minutes / 60:D2}:{minutes % 60:D2}";
    }
}
