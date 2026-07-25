using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Common.Persistence
{
    public static class DbInitializer
    {
        /// <summary>
        /// Applies pending migrations, seeds the initial School Admin account
        /// (FR-AUTH-07), and — for development — a small scheduling dataset so the
        /// CSP engine can be exercised end-to-end.
        /// </summary>
        public static async Task InitializeAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            await db.Database.MigrateAsync();

            await SeedSystemSettingsAsync(db);
            await SeedSuperAdminAsync(db, hasher, config);
            await SeedAdminAsync(db, hasher, config);
            await SeedStaffAsync(db, hasher);
            await SeedSchedulingSampleAsync(db, hasher);
            await SeedReturningStudentsAsync(db);
            await BackfillAcademicSetupAsync(db);
            await SeedStiProgramsAsync(db);
            await SeedFacultyLoadAsync(db);
            await SeedRichDemoDataAsync(db, hasher);
        }

        /// <summary>
        /// Creates the singleton system-parameters row at its default cap (FR-SCHED-05).
        /// Idempotent: databases seeded before this feature existed pick the row up on the
        /// next start, and an admin's edited cap is never reset back to the default.
        /// </summary>
        private static async Task SeedSystemSettingsAsync(AppDbContext db)
        {
            if (await db.SystemSettings.AnyAsync(s => s.Id == SystemSettings.SingletonId))
            {
                return;
            }

            db.SystemSettings.Add(new SystemSettings());
            await db.SaveChangesAsync();
        }

        /// <summary>
        /// Development-only: fills every corner of the system with realistic volume so each
        /// screen has something to show and broken flows surface early — Saturday + afternoon
        /// time slots, a second building and more rooms, a BSIT curriculum, BSCS year-2
        /// subjects with prerequisites, extra faculty with time preferences, cohorts and
        /// offerings for the active semester, distributed faculty loads, a batch of varied
        /// student registrations, and a finished (archived) prior school year. Every block is
        /// individually idempotent, so re-running is safe on any existing database.
        /// </summary>
        private static async Task SeedRichDemoDataAsync(AppDbContext db, IPasswordHasher<User> hasher)
        {
            var active = await db.Semesters.FirstOrDefaultAsync(s => s.IsActive);
            if (active is null)
            {
                return;
            }

            // ---- Time slots: afternoon blocks Mon–Fri and a Mon–Sat grid (schedule board shows Saturday) ----
            if (!await db.TimeSlots.AnyAsync(t => t.Day == DayOfWeek.Saturday))
            {
                var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
                var afternoon = new (int Start, int End)[] { (870, 960), (960, 1050) }; // 14:30–16:00, 16:00–17:30
                foreach (var d in weekdays)
                {
                    foreach (var (s, e) in afternoon)
                    {
                        if (!await db.TimeSlots.AnyAsync(t => t.Day == d && t.StartMinutes == s && t.EndMinutes == e))
                        {
                            db.TimeSlots.Add(new TimeSlot { Day = d, StartMinutes = s, EndMinutes = e });
                        }
                    }
                }

                var saturdayBlocks = new (int Start, int End)[] { (480, 570), (570, 660), (780, 870), (870, 960) };
                foreach (var (s, e) in saturdayBlocks)
                {
                    db.TimeSlots.Add(new TimeSlot { Day = DayOfWeek.Saturday, StartMinutes = s, EndMinutes = e });
                }
                await db.SaveChangesAsync();
            }

            // ---- A second building with more rooms (utilization panels need spread) ----
            if (!await db.Buildings.AnyAsync(b => b.Code == "AX"))
            {
                var annex = new Building { Name = "Annex Building", Code = "AX" };
                db.Buildings.Add(annex);
                db.Rooms.AddRange(
                    new Room { Name = "Annex 101", Capacity = 50, Kind = RoomKind.LectureRoom, BuildingId = annex.Id },
                    new Room { Name = "Annex 102", Capacity = 35, Kind = RoomKind.LectureRoom, BuildingId = annex.Id },
                    new Room { Name = "Computer Lab B", Capacity = 40, Kind = RoomKind.ComputerLaboratory, BuildingId = annex.Id },
                    new Room { Name = "AVR", Capacity = 60, Kind = RoomKind.LectureRoom, BuildingId = annex.Id });
                await db.SaveChangesAsync();
            }

            // ---- BSCS second-year subjects with prerequisite chains ----
            var bscs = await db.Curricula.FirstOrDefaultAsync(c => c.ProgramCode == "BSCS");
            if (bscs is not null && !await db.Subjects.AnyAsync(s => s.CurriculumId == bscs.Id && s.Code == "CS201"))
            {
                var cs101 = await db.Subjects.FirstOrDefaultAsync(s => s.CurriculumId == bscs.Id && s.Code == "CS101");
                var year2 = new[]
                {
                    new Subject { Code = "CS201", Title = "Data Structures and Algorithms", Units = 3, Hours = 3, Delivery = SubjectDelivery.LectureOnly, LectureHours = 3, ProgramCode = "BSCS", YearLevel = 2, Term = SemesterTerm.FirstSemester, CurriculumId = bscs.Id },
                    new Subject { Code = "CS202L", Title = "Object-Oriented Programming Laboratory", Units = 1, Hours = 3, Delivery = SubjectDelivery.LaboratoryOnly, LaboratoryHours = 3, LabRoomKind = RoomKind.ComputerLaboratory, ProgramCode = "BSCS", YearLevel = 2, Term = SemesterTerm.FirstSemester, CurriculumId = bscs.Id },
                    new Subject { Code = "MATH201", Title = "Discrete Mathematics", Units = 3, Hours = 3, Delivery = SubjectDelivery.LectureOnly, LectureHours = 3, ProgramCode = "BSCS", YearLevel = 2, Term = SemesterTerm.FirstSemester, CurriculumId = bscs.Id },
                    new Subject { Code = "GE201", Title = "Science, Technology and Society", Units = 3, Hours = 3, Delivery = SubjectDelivery.LectureOnly, LectureHours = 3, ProgramCode = "BSCS", YearLevel = 2, Term = SemesterTerm.FirstSemester, CurriculumId = bscs.Id },
                    new Subject { Code = "CS203", Title = "Computer Organization", Units = 3, Hours = 3, Delivery = SubjectDelivery.LectureOnly, LectureHours = 3, ProgramCode = "BSCS", YearLevel = 2, Term = SemesterTerm.SecondSemester, CurriculumId = bscs.Id }
                };
                db.Subjects.AddRange(year2);
                if (cs101 is not null)
                {
                    db.SubjectPrerequisites.AddRange(
                        new SubjectPrerequisite { SubjectId = year2[0].Id, PrerequisiteSubjectId = cs101.Id },
                        new SubjectPrerequisite { SubjectId = year2[1].Id, PrerequisiteSubjectId = cs101.Id });
                }
                // Same-term co-requisite: the OOP lab accompanies Data Structures.
                db.SubjectPrerequisites.Add(new SubjectPrerequisite { SubjectId = year2[1].Id, PrerequisiteSubjectId = year2[0].Id });
                await db.SaveChangesAsync();
            }

            // ---- A whole second program: BSIT curriculum, first-year subjects ----
            if (!await db.Curricula.AnyAsync(c => c.ProgramCode == "BSIT"))
            {
                var bsit = new Curriculum { ProgramCode = "BSIT", ProgramName = "BS Information Technology", IsActive = true };
                db.Curricula.Add(bsit);
                var year = await db.SchoolYears.FirstOrDefaultAsync(y => y.IsActive);
                if (year is not null)
                {
                    db.CurriculumSchoolYears.Add(new CurriculumSchoolYear { CurriculumId = bsit.Id, SchoolYearId = year.Id });
                }

                var it = new[]
                {
                    new Subject { Code = "IT101", Title = "Introduction to Information Technology", Units = 3, Hours = 3, Delivery = SubjectDelivery.LectureOnly, LectureHours = 3, ProgramCode = "BSIT", YearLevel = 1, Term = SemesterTerm.FirstSemester, CurriculumId = bsit.Id },
                    new Subject { Code = "IT102L", Title = "Computer Fundamentals Laboratory", Units = 1, Hours = 3, Delivery = SubjectDelivery.LaboratoryOnly, LaboratoryHours = 3, LabRoomKind = RoomKind.ComputerLaboratory, ProgramCode = "BSIT", YearLevel = 1, Term = SemesterTerm.FirstSemester, CurriculumId = bsit.Id },
                    new Subject { Code = "GE101", Title = "Understanding the Self", Units = 3, Hours = 3, Delivery = SubjectDelivery.LectureOnly, LectureHours = 3, ProgramCode = "BSIT", YearLevel = 1, Term = SemesterTerm.FirstSemester, CurriculumId = bsit.Id },
                    new Subject { Code = "FIL101", Title = "Komunikasyon sa Akademikong Filipino", Units = 3, Hours = 3, Delivery = SubjectDelivery.LectureOnly, LectureHours = 3, ProgramCode = "BSIT", YearLevel = 1, Term = SemesterTerm.FirstSemester, CurriculumId = bsit.Id },
                    new Subject { Code = "IT103", Title = "Web Systems Basics", Units = 3, Hours = 3, Delivery = SubjectDelivery.LectureOnly, LectureHours = 3, ProgramCode = "BSIT", YearLevel = 1, Term = SemesterTerm.SecondSemester, CurriculumId = bsit.Id }
                };
                db.Subjects.AddRange(it);
                db.SubjectPrerequisites.Add(new SubjectPrerequisite { SubjectId = it[4].Id, PrerequisiteSubjectId = it[0].Id });
                await db.SaveChangesAsync();
            }

            // ---- Extra faculty (BSIT needs its own teachers) + time preferences ----
            var newFaculty = new (string First, string Last, string Email, string Program)[]
            {
                ("Liza", "Navarro", "faculty4@stialaminos.local", "BSIT"),
                ("Marco", "Villanueva", "faculty5@stialaminos.local", "BSIT"),
                ("Elena", "Ramos", "faculty6@stialaminos.local", "BSCS")
            };
            foreach (var (first, last, email, program) in newFaculty)
            {
                if (await db.Users.AnyAsync(u => u.Email == email))
                {
                    continue;
                }
                var user = new User
                {
                    FirstName = first,
                    LastName = last,
                    Email = email,
                    Role = UserRole.FacultyMember,
                    TermsAcceptedAtUtc = DateTime.UtcNow
                };
                user.PasswordHash = hasher.HashPassword(user, "Faculty@Sengen2026");
                db.Users.Add(user);
                db.FacultyProfiles.Add(new FacultyProfile
                {
                    UserId = user.Id,
                    ProgramCode = program,
                    MaxLoadUnits = 24,
                    EmployeeId = $"STI-{1100 + Math.Abs(email.GetHashCode() % 800)}"
                });
            }
            await db.SaveChangesAsync();

            if (!await db.FacultyTimePreferences.AnyAsync())
            {
                var profiles = await db.FacultyProfiles.OrderBy(f => f.EmployeeId).Take(3).ToListAsync();
                foreach (var (profile, index) in profiles.Select((p, i) => (p, i)))
                {
                    // Alternate morning-person / afternoon-person windows.
                    var morning = index % 2 == 0;
                    foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday })
                    {
                        db.FacultyTimePreferences.Add(new FacultyTimePreference
                        {
                            FacultyProfileId = profile.Id,
                            Day = day,
                            StartMinutes = morning ? 480 : 780,
                            EndMinutes = morning ? 720 : 1050
                        });
                    }
                }
                await db.SaveChangesAsync();
            }

            // ---- Cohorts (class sections) and subject offerings for the active semester ----
            var wantedCohorts = new (string Program, int Year, string Block)[]
            {
                ("BSCS", 1, "A"), ("BSCS", 1, "B"), ("BSCS", 2, "A"),
                ("BSIT", 1, "A"), ("BSIT", 1, "B")
            };
            foreach (var (program, yearLevel, block) in wantedCohorts)
            {
                if (!await db.ClassSections.AnyAsync(c =>
                    c.SemesterId == active.Id && c.ProgramCode == program && c.YearLevel == yearLevel && c.SectionName == block))
                {
                    db.ClassSections.Add(new ClassSection
                    {
                        SemesterId = active.Id,
                        ProgramCode = program,
                        YearLevel = yearLevel,
                        SectionName = block
                    });
                }
            }
            await db.SaveChangesAsync();

            // Offer every first-semester, non-archived subject to its matching cohorts.
            var term = active.Term;
            var offerSubjects = await db.Subjects
                .Where(s => !s.IsArchived && s.Term == term)
                .ToListAsync();
            var cohorts = await db.ClassSections.Where(c => c.SemesterId == active.Id).ToListAsync();
            foreach (var cohort in cohorts)
            {
                foreach (var subject in offerSubjects.Where(s => s.ProgramCode == cohort.ProgramCode && s.YearLevel == cohort.YearLevel))
                {
                    var code = $"{cohort.ProgramCode}-{cohort.YearLevel}{cohort.SectionName}-{subject.Code}";
                    if (!await db.Sections.AnyAsync(x => x.SemesterId == active.Id && x.SectionCode == code))
                    {
                        db.Sections.Add(new Section
                        {
                            SubjectId = subject.Id,
                            SemesterId = active.Id,
                            SectionCode = code,
                            ProgramCode = cohort.ProgramCode,
                            YearLevel = cohort.YearLevel,
                            Block = cohort.SectionName,
                            Capacity = Section.DefaultCapacityCap
                        });
                    }
                }
            }
            await db.SaveChangesAsync();

            // ---- Faculty loads: round-robin each cohort's subjects across its program's faculty ----
            var facultyByProgram = (await db.FacultyProfiles.OrderBy(f => f.EmployeeId).ToListAsync())
                .GroupBy(f => f.ProgramCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var loadIndexByProgram = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var cohort in cohorts)
            {
                if (!facultyByProgram.TryGetValue(cohort.ProgramCode, out var pool) || pool.Count == 0)
                {
                    continue;
                }
                foreach (var subject in offerSubjects.Where(s => s.ProgramCode == cohort.ProgramCode && s.YearLevel == cohort.YearLevel))
                {
                    if (await db.FacultyLoadAssignments.AnyAsync(l =>
                        l.SemesterId == active.Id && l.SubjectId == subject.Id && l.ClassSectionId == cohort.Id))
                    {
                        continue;
                    }
                    var i = loadIndexByProgram.GetValueOrDefault(cohort.ProgramCode);
                    db.FacultyLoadAssignments.Add(new FacultyLoadAssignment
                    {
                        FacultyProfileId = pool[i % pool.Count].Id,
                        SubjectId = subject.Id,
                        ClassSectionId = cohort.Id,
                        SemesterId = active.Id
                    });
                    loadIndexByProgram[cohort.ProgramCode] = i + 1;
                }
            }
            await db.SaveChangesAsync();

            // ---- A batch of varied SIS registrations (statuses, docs, pre-authorization) ----
            if (await db.StudentRegistrations.CountAsync() < 8)
            {
                var people = new (string Number, string Last, string First, RegistrationStatus Status, ProgramTrack Track, bool PreAuth, DocumentStatus Docs)[]
                {
                    ("2026-000101", "Aquino",   "Paolo",     RegistrationStatus.Submitted, ProgramTrack.ITP, false, DocumentStatus.NotSubmitted),
                    ("2026-000102", "Bautista", "Karen",     RegistrationStatus.Submitted, ProgramTrack.ITP, false, DocumentStatus.XeroxCopy),
                    ("2026-000103", "Corpuz",   "Miguel",    RegistrationStatus.Confirmed, ProgramTrack.ITP, true,  DocumentStatus.Submitted),
                    ("2026-000104", "Domingo",  "Alyssa",    RegistrationStatus.Confirmed, ProgramTrack.HRS, true,  DocumentStatus.Submitted),
                    ("2026-000105", "Estrada",  "Ramon",     RegistrationStatus.Confirmed, ProgramTrack.HRA, false, DocumentStatus.XeroxCopy),
                    ("2026-000106", "Fernandez","Bianca",    RegistrationStatus.Rejected,  ProgramTrack.ITP, false, DocumentStatus.NotSubmitted),
                    ("2026-000107", "Garcia",   "Noel",      RegistrationStatus.Submitted, ProgramTrack.HRS, false, DocumentStatus.Submitted),
                    ("2026-000108", "Hernandez","Patricia",  RegistrationStatus.Confirmed, ProgramTrack.ITP, true,  DocumentStatus.Submitted)
                };

                var demoRequirements = await db.AdmissionRequirements
                    .Include(r => r.Programs).Where(r => r.IsActive).ToListAsync();

                foreach (var p in people)
                {
                    if (await db.StudentRegistrations.AnyAsync(r => r.StudentNumber == p.Number))
                    {
                        continue;
                    }
                    var reg = new StudentRegistration
                    {
                        StudentNumber = p.Number,
                        Status = p.Status,
                        StudentType = StudentType.NewStudent,
                        Program = p.Track,
                        SemesterId = active.Id,
                        IsPreAuthorized = p.PreAuth,
                        LastName = p.Last,
                        FirstName = p.First,
                        MiddleName = "Demo",
                        DateOfBirth = new DateOnly(2007, 1 + Math.Abs(p.Number.GetHashCode() % 12), 1 + Math.Abs(p.Number.GetHashCode() % 27)),
                        Birthplace = "Alaminos City, Pangasinan",
                        Citizenship = "Filipino",
                        CivilStatus = CivilStatus.Single,
                        Gender = p.First.EndsWith('a') ? Gender.Female : Gender.Male,
                        Email = "noreply.classsched.stialam@gmail.com",
                        MobileNumber = "09170001234",
                        AddressLine = "Demo St.",
                        Barangay = "Poblacion",
                        CityMunicipality = "Alaminos City",
                        Province = "Pangasinan",
                        ZipCode = "2404",
                        LastSchoolLevel = LastSchoolLevel.SeniorHighSchool,
                        SchoolName = "STI College Alaminos",
                        SchoolProgram = "ICT",
                        SchoolYear = "2025-2026",
                        YearGradeLastAttended = YearGradeLevel.Grade12,
                        LastTerm = AcademicTerm.Second,
                        GuardianRelationship = GuardianRelationship.Mother,
                        GuardianName = $"Guardian {p.Last}",
                        GuardianMobile = "09170005678",
                        TermsAcceptedAtUtc = DateTime.UtcNow.AddDays(-Math.Abs(p.Number.GetHashCode() % 30))
                    };
                    foreach (var req in demoRequirements.Where(r => r.Programs.Any(pr => pr.Program == reg.Program)))
                    {
                        reg.Documents.Add(new RegistrationDocument { RequirementCode = req.Code, Status = p.Docs });
                    }
                    db.StudentRegistrations.Add(reg);
                }
                await db.SaveChangesAsync();
            }

            // ---- A finished prior school year whose first semester is archived (frozen schedule demo) ----
            if (!await db.SchoolYears.AnyAsync(y => y.Name == "AY 2025-2026"))
            {
                var prior = new SchoolYear
                {
                    Name = "AY 2025-2026",
                    IsActive = false,
                    StartDate = new DateOnly(2025, 8, 11),
                    EndDate = new DateOnly(2026, 6, 30)
                };
                db.SchoolYears.Add(prior);
                db.Semesters.AddRange(
                    new Semester
                    {
                        Name = "AY 2025-2026 — First Semester",
                        Term = SemesterTerm.FirstSemester,
                        IsActive = false,
                        IsArchived = true,
                        ArchivedAtUtc = DateTime.UtcNow.AddMonths(-6),
                        StartDate = new DateOnly(2025, 8, 11),
                        EndDate = new DateOnly(2025, 12, 19),
                        SchoolYearId = prior.Id
                    },
                    new Semester
                    {
                        Name = "AY 2025-2026 — Second Semester",
                        Term = SemesterTerm.SecondSemester,
                        IsActive = false,
                        IsArchived = true,
                        ArchivedAtUtc = DateTime.UtcNow.AddMonths(-1),
                        StartDate = new DateOnly(2026, 1, 12),
                        EndDate = new DateOnly(2026, 5, 29),
                        SchoolYearId = prior.Id
                    });
                await db.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Development-only: give the first faculty member a small starting load in the active
        /// semester so the Faculty Load screen has data to show. Idempotent — runs only once.
        /// </summary>
        /// <summary>
        /// The programs STI Alaminos actually offers — HRA (3-yr Hotel and Restaurant
        /// Administration), HRS (2-yr Hospitality and Restaurant Services), and ITP (2-yr
        /// Information Technology) — plus the one Kitchen Laboratory the campus has.
        /// <para>
        /// The subjects deliberately span all three delivery modes, because that is what makes
        /// the room-kind hard constraint testable: ITP laboratory hours need the Computer
        /// Laboratory, HRA/HRS laboratory hours need the Kitchen Laboratory, and the campus has
        /// exactly one of each, so the two programs genuinely contend for time.
        /// </para>
        /// Each block is idempotent, keyed on the program code / room name.
        /// </summary>
        private static async Task SeedStiProgramsAsync(AppDbContext db)
        {
            if (!await db.Rooms.AnyAsync(r => r.Kind == RoomKind.KitchenLaboratory))
            {
                var building = await db.Buildings.OrderBy(b => b.Name).FirstOrDefaultAsync();
                if (building is not null)
                {
                    db.Rooms.Add(new Room
                    {
                        Name = "Kitchen Laboratory",
                        Capacity = 30,
                        Kind = RoomKind.KitchenLaboratory,
                        BuildingId = building.Id
                    });
                    await db.SaveChangesAsync();
                }
            }

            var schoolYear = await db.SchoolYears.FirstOrDefaultAsync(y => y.IsActive);

            // (code, title, units, delivery, lecture hrs, lab hrs, lab kind, year, term)
            var programs = new (string Code, string Name, (string Code, string Title, int Units,
                SubjectDelivery Delivery, int Lec, int Lab, RoomKind? LabKind, int Year, SemesterTerm Term)[] Subjects)[]
            {
                ("ITP", "2-yr Information Technology Program",
                [
                    ("ITP101", "Computer Programming 1", 3, SubjectDelivery.LectureLaboratory, 2, 3, RoomKind.ComputerLaboratory, 1, SemesterTerm.FirstSemester),
                    ("ITP102", "Web Design and Development", 3, SubjectDelivery.LectureLaboratory, 2, 3, RoomKind.ComputerLaboratory, 1, SemesterTerm.FirstSemester),
                    ("ITP111", "Purposive Communication", 3, SubjectDelivery.LectureOnly, 3, 0, null, 1, SemesterTerm.FirstSemester),
                    ("ITP103", "Database Management Systems", 3, SubjectDelivery.LectureLaboratory, 2, 3, RoomKind.ComputerLaboratory, 1, SemesterTerm.SecondSemester),
                    ("ITP201", "Networking 1", 3, SubjectDelivery.LectureLaboratory, 2, 3, RoomKind.ComputerLaboratory, 2, SemesterTerm.FirstSemester),
                    ("ITP202", "Systems Analysis and Design", 3, SubjectDelivery.LectureOnly, 3, 0, null, 2, SemesterTerm.SecondSemester)
                ]),
                ("HRA", "3-yr Hotel and Restaurant Administration",
                [
                    ("HRA101", "Fundamentals of Food Service Operations", 3, SubjectDelivery.LectureLaboratory, 2, 3, RoomKind.KitchenLaboratory, 1, SemesterTerm.FirstSemester),
                    ("HRA102", "Introduction to the Hospitality Industry", 3, SubjectDelivery.LectureOnly, 3, 0, null, 1, SemesterTerm.FirstSemester),
                    ("HRA103", "Culinary Arts 1", 2, SubjectDelivery.LaboratoryOnly, 0, 3, RoomKind.KitchenLaboratory, 1, SemesterTerm.SecondSemester),
                    ("HRA201", "Food and Beverage Service", 3, SubjectDelivery.LectureLaboratory, 2, 3, RoomKind.KitchenLaboratory, 2, SemesterTerm.FirstSemester),
                    ("HRA202", "Front Office Management", 3, SubjectDelivery.LectureOnly, 3, 0, null, 2, SemesterTerm.SecondSemester),
                    ("HRA301", "Hospitality Operations Practicum", 3, SubjectDelivery.LectureOnly, 3, 0, null, 3, SemesterTerm.FirstSemester)
                ]),
                ("HRS", "2-yr Hospitality and Restaurant Services",
                [
                    ("HRS101", "Basic Food Preparation", 3, SubjectDelivery.LectureLaboratory, 2, 3, RoomKind.KitchenLaboratory, 1, SemesterTerm.FirstSemester),
                    ("HRS102", "Housekeeping Services", 3, SubjectDelivery.LectureOnly, 3, 0, null, 1, SemesterTerm.FirstSemester),
                    ("HRS103", "Bartending and Beverage Service", 2, SubjectDelivery.LaboratoryOnly, 0, 3, RoomKind.KitchenLaboratory, 1, SemesterTerm.SecondSemester),
                    ("HRS201", "Front Office Services", 3, SubjectDelivery.LectureOnly, 3, 0, null, 2, SemesterTerm.FirstSemester)
                ])
            };

            foreach (var (code, name, subjects) in programs)
            {
                if (await db.Curricula.AnyAsync(c => c.ProgramCode == code))
                {
                    continue;
                }

                var curriculum = new Curriculum { ProgramCode = code, ProgramName = name, IsActive = true };
                db.Curricula.Add(curriculum);
                if (schoolYear is not null)
                {
                    db.CurriculumSchoolYears.Add(
                        new CurriculumSchoolYear { CurriculumId = curriculum.Id, SchoolYearId = schoolYear.Id });
                }

                foreach (var s in subjects)
                {
                    db.Subjects.Add(new Subject
                    {
                        Code = s.Code,
                        Title = s.Title,
                        Units = s.Units,
                        Delivery = s.Delivery,
                        LectureHours = s.Lec,
                        LaboratoryHours = s.Lab,
                        LabRoomKind = s.LabKind,
                        Hours = s.Lec + s.Lab,
                        ProgramCode = code,
                        YearLevel = s.Year,
                        Term = s.Term,
                        CurriculumId = curriculum.Id
                    });
                }
            }

            await db.SaveChangesAsync();
        }

        private static async Task SeedFacultyLoadAsync(AppDbContext db)
        {
            if (await db.FacultyLoadAssignments.AnyAsync())
            {
                return;
            }

            var semester = await db.Semesters.FirstOrDefaultAsync(s => s.IsActive);
            var faculty = await db.FacultyProfiles.OrderBy(f => f.Id).FirstOrDefaultAsync();
            if (semester is null || faculty is null)
            {
                return;
            }

            var subjects = await db.Subjects
                .Where(s => s.ProgramCode == faculty.ProgramCode)
                .OrderBy(s => s.YearLevel).ThenBy(s => s.Code)
                .Take(2)
                .ToListAsync();
            if (subjects.Count == 0)
            {
                return;
            }

            // Assignments are per class section; ensure a "Section A" block exists for each
            // (program, year) the seeded subjects belong to, then assign them to that block.
            // Sections created here are cached by (program, year) so two subjects at the same
            // year reuse the one block instead of inserting a duplicate (unique-index violation).
            var sectionByCohort = new Dictionary<(string, int), ClassSection>();
            foreach (var subject in subjects)
            {
                var cohort = (subject.ProgramCode, subject.YearLevel);
                if (!sectionByCohort.TryGetValue(cohort, out var section))
                {
                    section = await db.ClassSections.FirstOrDefaultAsync(c =>
                        c.SemesterId == semester.Id && c.ProgramCode == subject.ProgramCode
                        && c.YearLevel == subject.YearLevel && c.SectionName == "A");
                    if (section is null)
                    {
                        section = new ClassSection
                        {
                            SemesterId = semester.Id,
                            ProgramCode = subject.ProgramCode,
                            YearLevel = subject.YearLevel,
                            SectionName = "A"
                        };
                        db.ClassSections.Add(section);
                    }
                    sectionByCohort[cohort] = section;
                }

                db.FacultyLoadAssignments.Add(new FacultyLoadAssignment
                {
                    FacultyProfileId = faculty.Id,
                    SubjectId = subject.Id,
                    ClassSectionId = section.Id,
                    SemesterId = semester.Id
                });
            }

            await db.SaveChangesAsync();
        }

        /// <summary>
        /// Idempotent: links any semesters/rooms that predate the school-year and building
        /// relationships (added later) to a default parent, so a database seeded before those
        /// columns existed stays coherent. On a freshly seeded database this finds nothing to do.
        /// </summary>
        private static async Task BackfillAcademicSetupAsync(AppDbContext db)
        {
            var changed = false;

            // Employee IDs arrived with the faculty-loading reports; give pre-existing
            // profiles a stable sequential number (idempotent: only fills blanks).
            var withoutEmployeeId = await db.FacultyProfiles
                .Where(f => f.EmployeeId == string.Empty)
                .OrderBy(f => f.Id)
                .ToListAsync();
            if (withoutEmployeeId.Count > 0)
            {
                var next = 1000 + await db.FacultyProfiles.CountAsync(f => f.EmployeeId != string.Empty);
                foreach (var profile in withoutEmployeeId)
                {
                    profile.EmployeeId = $"STI-{++next}";
                }
                changed = true;
            }

            if (await db.Semesters.AnyAsync(s => s.SchoolYearId == null))
            {
                var schoolYear = await db.SchoolYears.FirstOrDefaultAsync(y => y.IsActive)
                    ?? await db.SchoolYears.OrderByDescending(y => y.StartDate).FirstOrDefaultAsync();
                if (schoolYear is null)
                {
                    schoolYear = new SchoolYear
                    {
                        Name = "AY 2026-2027",
                        IsActive = true,
                        StartDate = new DateOnly(2026, 8, 10),
                        EndDate = new DateOnly(2027, 6, 30)
                    };
                    db.SchoolYears.Add(schoolYear);
                }

                foreach (var semester in await db.Semesters.Where(s => s.SchoolYearId == null).ToListAsync())
                {
                    semester.SchoolYearId = schoolYear.Id;
                }
                changed = true;
            }

            if (await db.Rooms.AnyAsync(r => r.BuildingId == null))
            {
                var building = await db.Buildings.OrderBy(b => b.Name).FirstOrDefaultAsync();
                if (building is null)
                {
                    building = new Building { Name = "Main Building", Code = "MB" };
                    db.Buildings.Add(building);
                }

                foreach (var room in await db.Rooms.Where(r => r.BuildingId == null).ToListAsync())
                {
                    room.BuildingId = building.Id;
                }
                changed = true;
            }

            // Group any subjects that predate curricula under one curriculum per program code.
            if (await db.Subjects.AnyAsync(s => s.CurriculumId == null))
            {
                var programCodes = await db.Subjects
                    .Where(s => s.CurriculumId == null)
                    .Select(s => s.ProgramCode)
                    .Distinct()
                    .ToListAsync();

                foreach (var programCode in programCodes)
                {
                    var curriculum = await db.Curricula.FirstOrDefaultAsync(c => c.ProgramCode == programCode)
                        ?? db.Curricula.Add(new Curriculum
                        {
                            ProgramCode = programCode,
                            ProgramName = programCode,
                            IsActive = true
                        }).Entity;

                    foreach (var subject in await db.Subjects.Where(s => s.CurriculumId == null && s.ProgramCode == programCode).ToListAsync())
                    {
                        subject.CurriculumId = curriculum.Id;
                    }
                }
                changed = true;
            }

            // Link any curriculum that has no school-year effectivity yet (e.g. rows that predate the
            // school-year links) to the active school year, so it stays connected.
            if (await db.SchoolYears.AnyAsync() && await db.Curricula.AnyAsync(c => !c.SchoolYears.Any()))
            {
                var defaultYear = await db.SchoolYears.FirstOrDefaultAsync(y => y.IsActive)
                    ?? await db.SchoolYears.OrderByDescending(y => y.StartDate).FirstAsync();
                foreach (var curriculum in await db.Curricula.Where(c => !c.SchoolYears.Any()).ToListAsync())
                {
                    // Don't collide with another curriculum of the same program already on that year.
                    var taken = await db.CurriculumSchoolYears.AnyAsync(l =>
                        l.SchoolYearId == defaultYear.Id && l.Curriculum!.ProgramCode == curriculum.ProgramCode);
                    if (!taken)
                    {
                        db.CurriculumSchoolYears.Add(new CurriculumSchoolYear { CurriculumId = curriculum.Id, SchoolYearId = defaultYear.Id });
                    }
                }
                changed = true;
            }

            // Class sections predate the per-cohort curriculum link — attach each to its program's
            // active curriculum (fallback: any curriculum for that program) so faculty-load offers
            // and schedule generation stay scoped once the field exists (FR-SCHED-04). Idempotent:
            // only fills rows still missing a curriculum.
            if (await db.ClassSections.AnyAsync(c => c.CurriculumId == null))
            {
                var curricula = await db.Curricula
                    .Select(c => new { c.Id, c.ProgramCode, c.IsActive })
                    .ToListAsync();
                var activeByProgram = curricula
                    .GroupBy(c => c.ProgramCode)
                    .ToDictionary(
                        g => g.Key,
                        g => (g.FirstOrDefault(c => c.IsActive) ?? g.First()).Id,
                        StringComparer.OrdinalIgnoreCase);

                foreach (var section in await db.ClassSections.Where(c => c.CurriculumId == null).ToListAsync())
                {
                    if (activeByProgram.TryGetValue(section.ProgramCode, out var curriculumId))
                    {
                        section.CurriculumId = curriculumId;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                await db.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Dev staff accounts for the role-gated slices. Idempotent per-email so accounts added
        /// after the scheduling sample already exists (e.g. the Admission Officer) still get seeded.
        /// </summary>
        private static async Task SeedStaffAsync(AppDbContext db, IPasswordHasher<User> hasher)
        {
            var staff = new[]
            {
                (UserRole.AcademicHead, "academichead@stialaminos.local"),
                (UserRole.Registrar, "registrar@stialaminos.local"),
                (UserRole.AdmissionOfficer, "admissionofficer@stialaminos.local")
            };

            var added = false;
            foreach (var (role, email) in staff)
            {
                if (await db.Users.AnyAsync(u => u.Email == email))
                {
                    continue;
                }

                var user = new User
                {
                    FirstName = "Dev",
                    LastName = role.ToString(),
                    Email = email,
                    Role = role,
                    TermsAcceptedAtUtc = DateTime.UtcNow
                };
                user.PasswordHash = hasher.HashPassword(user, "Staff@Sengen2026");
                db.Users.Add(user);
                added = true;
            }

            if (added)
            {
                await db.SaveChangesAsync();
            }
        }

        /// <summary>
        /// The top-of-hierarchy Super Admin (FR-AUTH) — owns user management and the ISO 25010
        /// rating survey. Seeded once; credentials come from config (Seed:SuperAdminEmail/Password)
        /// with dev defaults. Idempotent on the SuperAdmin role.
        /// </summary>
        private static async Task SeedSuperAdminAsync(AppDbContext db, IPasswordHasher<User> hasher, IConfiguration config)
        {
            if (await db.Users.AnyAsync(u => u.Role == UserRole.SuperAdmin))
            {
                return;
            }

            var superAdmin = new User
            {
                FirstName = "Super",
                LastName = "Admin",
                Email = config["Seed:SuperAdminEmail"] ?? "superadmin@stialaminos.local",
                Role = UserRole.SuperAdmin,
                TermsAcceptedAtUtc = DateTime.UtcNow
            };
            superAdmin.PasswordHash = hasher.HashPassword(superAdmin, config["Seed:SuperAdminPassword"] ?? "ChangeMe123!");

            db.Users.Add(superAdmin);
            db.AuditEntries.Add(new AuditEntry
            {
                Action = AuditAction.UserAccountCreated,
                Summary = "System initialized; seeded the Super Admin account.",
                ActorUserId = superAdmin.Id,
                ActorName = superAdmin.FullName,
                ActorRole = superAdmin.Role.ToString(),
                EntityType = "User",
                EntityId = superAdmin.Id.ToString()
            });

            await db.SaveChangesAsync();
        }

        private static async Task SeedAdminAsync(AppDbContext db, IPasswordHasher<User> hasher, IConfiguration config)
        {
            if (await db.Users.AnyAsync(u => u.Role == UserRole.SchoolAdmin))
            {
                return;
            }

            var admin = new User
            {
                FirstName = "School",
                LastName = "Admin",
                Email = config["Seed:AdminEmail"] ?? "admin@stialaminos.local",
                Role = UserRole.SchoolAdmin,
                TermsAcceptedAtUtc = DateTime.UtcNow
            };
            admin.PasswordHash = hasher.HashPassword(admin, config["Seed:AdminPassword"] ?? "ChangeMe123!");

            db.Users.Add(admin);

            // Bootstrap audit entry (FR-AUD-01) so the trail is populated from first run.
            db.AuditEntries.Add(new AuditEntry
            {
                Action = AuditAction.UserAccountCreated,
                Summary = "System initialized; seeded the School Admin account.",
                ActorUserId = admin.Id,
                ActorName = admin.FullName,
                ActorRole = admin.Role.ToString(),
                EntityType = "User",
                EntityId = admin.Id.ToString()
            });

            await db.SaveChangesAsync();
        }

        /// <summary>
        /// Development-only sample so /api/scheduling/generate has a solvable problem:
        /// one active semester, four rooms (one lab), a Mon–Fri time-slot grid, five BSCS
        /// year-1 subjects, three faculty, and two cohorts (blocks A and B).
        /// </summary>
        private static async Task SeedSchedulingSampleAsync(AppDbContext db, IPasswordHasher<User> hasher)
        {
            if (await db.Semesters.AnyAsync())
            {
                return;
            }

            var schoolYear = new SchoolYear
            {
                Name = "AY 2026-2027",
                IsActive = true,
                StartDate = new DateOnly(2026, 8, 10),
                EndDate = new DateOnly(2027, 6, 30)
            };
            db.SchoolYears.Add(schoolYear);

            var semester = new Semester
            {
                Name = "AY 2026-2027 — First Semester",
                Term = SemesterTerm.FirstSemester,
                IsActive = true,
                StartDate = new DateOnly(2026, 8, 10),
                EndDate = new DateOnly(2026, 12, 18),
                SchoolYearId = schoolYear.Id
            };
            db.Semesters.Add(semester);

            var mainBuilding = new Building { Name = "Main Building", Code = "MB" };
            db.Buildings.Add(mainBuilding);

            var rooms = new[]
            {
                new Room { Name = "Room 301", Capacity = 45, Kind = RoomKind.LectureRoom, BuildingId = mainBuilding.Id },
                new Room { Name = "Room 302", Capacity = 45, Kind = RoomKind.LectureRoom, BuildingId = mainBuilding.Id },
                new Room { Name = "Room 201", Capacity = 40, Kind = RoomKind.LectureRoom, BuildingId = mainBuilding.Id },
                new Room { Name = "Computer Lab A", Capacity = 40, Kind = RoomKind.ComputerLaboratory, BuildingId = mainBuilding.Id }
            };
            db.Rooms.AddRange(rooms);

            // Mon–Fri × three 90-minute blocks (minutes-from-midnight).
            var blocks = new (int Start, int End)[] { (480, 570), (570, 660), (780, 870) };
            var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
            var timeSlots = (from d in days from b in blocks select new TimeSlot { Day = d, StartMinutes = b.Start, EndMinutes = b.End }).ToArray();
            db.TimeSlots.AddRange(timeSlots);

            const string program = "BSCS";
            var curriculum = new Curriculum
            {
                ProgramCode = program,
                ProgramName = "BS Computer Science",
                IsActive = true
            };
            db.Curricula.Add(curriculum);
            db.CurriculumSchoolYears.Add(new CurriculumSchoolYear { CurriculumId = curriculum.Id, SchoolYearId = schoolYear.Id });

            var subjects = new[]
            {
                new Subject { Code = "CS101", Title = "Introduction to Computing", Units = 3, Hours = 3, Delivery = SubjectDelivery.LectureOnly, LectureHours = 3, ProgramCode = program, YearLevel = 1, Term = SemesterTerm.FirstSemester, CurriculumId = curriculum.Id },
                new Subject { Code = "MATH101", Title = "College Algebra", Units = 3, Hours = 3, Delivery = SubjectDelivery.LectureOnly, LectureHours = 3, ProgramCode = program, YearLevel = 1, Term = SemesterTerm.FirstSemester, CurriculumId = curriculum.Id },
                new Subject { Code = "ENG101", Title = "Purposive Communication", Units = 3, Hours = 3, Delivery = SubjectDelivery.LectureOnly, LectureHours = 3, ProgramCode = program, YearLevel = 1, Term = SemesterTerm.FirstSemester, CurriculumId = curriculum.Id },
                new Subject { Code = "PE101", Title = "Physical Education 1", Units = 2, Hours = 2, Delivery = SubjectDelivery.LectureOnly, LectureHours = 2, ProgramCode = program, YearLevel = 1, Term = SemesterTerm.SecondSemester, CurriculumId = curriculum.Id },
                new Subject { Code = "CS102L", Title = "Programming Laboratory", Units = 1, Hours = 3, Delivery = SubjectDelivery.LaboratoryOnly, LaboratoryHours = 3, LabRoomKind = RoomKind.ComputerLaboratory, ProgramCode = program, YearLevel = 1, Term = SemesterTerm.SecondSemester, CurriculumId = curriculum.Id }
            };
            db.Subjects.AddRange(subjects);

            // Sample prerequisite: the programming lab requires Introduction to Computing.
            db.SubjectPrerequisites.Add(new SubjectPrerequisite
            {
                SubjectId = subjects[4].Id,
                PrerequisiteSubjectId = subjects[0].Id
            });

            var facultyProfiles = new List<FacultyProfile>();
            for (var i = 1; i <= 3; i++)
            {
                var user = new User
                {
                    FirstName = "Faculty",
                    LastName = $"Member {i}",
                    Email = $"faculty{i}@stialaminos.local",
                    Role = UserRole.FacultyMember,
                    TermsAcceptedAtUtc = DateTime.UtcNow
                };
                user.PasswordHash = hasher.HashPassword(user, "Faculty@Sengen2026");
                db.Users.Add(user);

                facultyProfiles.Add(new FacultyProfile
                {
                    UserId = user.Id,
                    ProgramCode = program,
                    MaxLoadUnits = 24,
                    EmployeeId = $"STI-{1000 + i}"
                });
            }
            db.FacultyProfiles.AddRange(facultyProfiles);

            // Two student cohorts (blocks A and B), each taking all five subjects.
            foreach (var block in new[] { "A", "B" })
            {
                foreach (var subject in subjects)
                {
                    db.Sections.Add(new Section
                    {
                        SubjectId = subject.Id,
                        SemesterId = semester.Id,
                        SectionCode = $"{program}-1{block}-{subject.Code}",
                        ProgramCode = program,
                        YearLevel = 1,
                        Block = block,
                        Capacity = 40
                    });
                }
            }

            await db.SaveChangesAsync();
        }

        /// <summary>
        /// Development-only: a couple of confirmed prior-term SIS records with known student numbers
        /// so the returning-student term-activation flow can be exercised end-to-end. Uses a real
        /// inbox placeholder for the email; change it to observe live confirmation mail.
        /// </summary>
        private static async Task SeedReturningStudentsAsync(AppDbContext db)
        {
            if (await db.StudentRegistrations.AnyAsync())
            {
                return;
            }

            var semester = await db.Semesters.FirstOrDefaultAsync(s => s.IsActive);
            if (semester is null)
            {
                return;
            }

            var seeds = new[]
            {
                new StudentRegistration
                {
                    StudentNumber = "2025-000001",
                    Status = RegistrationStatus.Confirmed,
                    StudentType = StudentType.NewStudent,
                    Program = ProgramTrack.ITP,
                    SemesterId = semester.Id,
                    LastName = "Santos",
                    FirstName = "Maria Clara",
                    MiddleName = "Reyes",
                    DateOfBirth = new DateOnly(2006, 5, 14),
                    Birthplace = "Alaminos City, Pangasinan",
                    Citizenship = "Filipino",
                    CivilStatus = CivilStatus.Single,
                    Gender = Gender.Female,
                    Email = "noreply.classsched.stialam@gmail.com",
                    MobileNumber = "09171234567",
                    AddressLine = "12 Rizal St.",
                    Barangay = "Poblacion",
                    CityMunicipality = "Alaminos City",
                    Province = "Pangasinan",
                    ZipCode = "2404",
                    LastSchoolLevel = LastSchoolLevel.SeniorHighSchool,
                    SchoolName = "STI College Alaminos",
                    SchoolProgram = "ICT",
                    SchoolYear = "2024-2025",
                    YearGradeLastAttended = YearGradeLevel.Grade12,
                    LastTerm = AcademicTerm.Second,
                    FatherName = "Jose Santos",
                    FatherMobile = "09170000001",
                    MotherName = "Ana Santos",
                    MotherMobile = "09170000002",
                    GuardianRelationship = GuardianRelationship.Mother,
                    GuardianName = "Ana Santos",
                    GuardianMobile = "09170000002",
                    TermsAcceptedAtUtc = DateTime.UtcNow.AddMonths(-8)
                },
                new StudentRegistration
                {
                    StudentNumber = "2025-000002",
                    Status = RegistrationStatus.Confirmed,
                    StudentType = StudentType.Transferee,
                    Program = ProgramTrack.HRA,
                    SemesterId = semester.Id,
                    LastName = "Dela Cruz",
                    FirstName = "Juan",
                    MiddleName = "Cruz",
                    DateOfBirth = new DateOnly(2005, 11, 2),
                    Birthplace = "Dagupan City, Pangasinan",
                    Citizenship = "Filipino",
                    CivilStatus = CivilStatus.Single,
                    Gender = Gender.Male,
                    Email = "noreply.classsched.stialam@gmail.com",
                    MobileNumber = "09189876543",
                    AddressLine = "5 Mabini St.",
                    Barangay = "San Jose",
                    CityMunicipality = "Alaminos City",
                    Province = "Pangasinan",
                    ZipCode = "2404",
                    LastSchoolLevel = LastSchoolLevel.College,
                    SchoolName = "Another University",
                    SchoolProgram = "BS Tourism",
                    SchoolYear = "2024-2025",
                    YearGradeLastAttended = YearGradeLevel.FirstYear,
                    LastTerm = AcademicTerm.Second,
                    FatherName = "Pedro Dela Cruz",
                    FatherMobile = "09170000003",
                    MotherName = "Rosa Dela Cruz",
                    MotherMobile = "09170000004",
                    GuardianRelationship = GuardianRelationship.Father,
                    GuardianName = "Pedro Dela Cruz",
                    GuardianMobile = "09170000003",
                    TermsAcceptedAtUtc = DateTime.UtcNow.AddMonths(-8)
                }
            };

            var seedRequirements = await db.AdmissionRequirements
                .Include(r => r.Programs).Where(r => r.IsActive).ToListAsync();

            foreach (var reg in seeds)
            {
                foreach (var req in seedRequirements.Where(r => r.Programs.Any(pr => pr.Program == reg.Program)))
                {
                    reg.Documents.Add(new RegistrationDocument
                    {
                        RequirementCode = req.Code,
                        Status = DocumentStatus.Submitted
                    });
                }
                db.StudentRegistrations.Add(reg);
            }

            await db.SaveChangesAsync();
        }
    }
}
