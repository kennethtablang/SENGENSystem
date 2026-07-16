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

            await SeedAdminAsync(db, hasher, config);
            await SeedStaffAsync(db, hasher);
            await SeedSchedulingSampleAsync(db, hasher);
            await SeedReturningStudentsAsync(db);
            await BackfillAcademicSetupAsync(db);
            await SeedFacultyLoadAsync(db);
        }

        /// <summary>
        /// Development-only: give the first faculty member a small starting load in the active
        /// semester so the Faculty Load screen has data to show. Idempotent — runs only once.
        /// </summary>
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
                new Room { Name = "Room 301", Capacity = 45, IsLaboratory = false, BuildingId = mainBuilding.Id },
                new Room { Name = "Room 302", Capacity = 45, IsLaboratory = false, BuildingId = mainBuilding.Id },
                new Room { Name = "Room 201", Capacity = 40, IsLaboratory = false, BuildingId = mainBuilding.Id },
                new Room { Name = "Computer Lab A", Capacity = 40, IsLaboratory = true, BuildingId = mainBuilding.Id }
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
                new Subject { Code = "CS101", Title = "Introduction to Computing", Units = 3, Hours = 3, ProgramCode = program, YearLevel = 1, Term = SemesterTerm.FirstSemester, CurriculumId = curriculum.Id },
                new Subject { Code = "MATH101", Title = "College Algebra", Units = 3, Hours = 3, ProgramCode = program, YearLevel = 1, Term = SemesterTerm.FirstSemester, CurriculumId = curriculum.Id },
                new Subject { Code = "ENG101", Title = "Purposive Communication", Units = 3, Hours = 3, ProgramCode = program, YearLevel = 1, Term = SemesterTerm.FirstSemester, CurriculumId = curriculum.Id },
                new Subject { Code = "PE101", Title = "Physical Education 1", Units = 2, Hours = 2, ProgramCode = program, YearLevel = 1, Term = SemesterTerm.SecondSemester, CurriculumId = curriculum.Id },
                new Subject { Code = "CS102L", Title = "Programming Laboratory", Units = 1, Hours = 3, ProgramCode = program, YearLevel = 1, RequiresLaboratory = true, Term = SemesterTerm.SecondSemester, CurriculumId = curriculum.Id }
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
                    MaxLoadUnits = 24
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

            foreach (var reg in seeds)
            {
                foreach (var docType in Enum.GetValues<DocumentType>())
                {
                    reg.Documents.Add(new RegistrationDocument
                    {
                        DocumentType = docType,
                        Status = DocumentStatus.Submitted
                    });
                }
                db.StudentRegistrations.Add(reg);
            }

            await db.SaveChangesAsync();
        }
    }
}
