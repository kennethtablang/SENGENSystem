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
            await SeedSchedulingSampleAsync(db, hasher);
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

            var semester = new Semester
            {
                Name = "AY 2026-2027 First Semester",
                IsActive = true,
                StartDate = new DateOnly(2026, 8, 10),
                EndDate = new DateOnly(2026, 12, 18)
            };
            db.Semesters.Add(semester);

            var rooms = new[]
            {
                new Room { Name = "Room 301", Capacity = 45, IsLaboratory = false },
                new Room { Name = "Room 302", Capacity = 45, IsLaboratory = false },
                new Room { Name = "Room 201", Capacity = 40, IsLaboratory = false },
                new Room { Name = "Computer Lab A", Capacity = 40, IsLaboratory = true }
            };
            db.Rooms.AddRange(rooms);

            // Mon–Fri × three 90-minute blocks (minutes-from-midnight).
            var blocks = new (int Start, int End)[] { (480, 570), (570, 660), (780, 870) };
            var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
            var timeSlots = (from d in days from b in blocks select new TimeSlot { Day = d, StartMinutes = b.Start, EndMinutes = b.End }).ToArray();
            db.TimeSlots.AddRange(timeSlots);

            const string program = "BSCS";
            var subjects = new[]
            {
                new Subject { Code = "CS101", Title = "Introduction to Computing", Units = 3, ProgramCode = program, YearLevel = 1 },
                new Subject { Code = "MATH101", Title = "College Algebra", Units = 3, ProgramCode = program, YearLevel = 1 },
                new Subject { Code = "ENG101", Title = "Purposive Communication", Units = 3, ProgramCode = program, YearLevel = 1 },
                new Subject { Code = "PE101", Title = "Physical Education 1", Units = 2, ProgramCode = program, YearLevel = 1 },
                new Subject { Code = "CS102L", Title = "Programming Laboratory", Units = 1, ProgramCode = program, YearLevel = 1, RequiresLaboratory = true }
            };
            db.Subjects.AddRange(subjects);

            // Dev staff accounts so the role-gated scheduling slices can be operated end-to-end.
            foreach (var (role, email) in new[]
            {
                (UserRole.AcademicHead, "academichead@stialaminos.local"),
                (UserRole.Registrar, "registrar@stialaminos.local")
            })
            {
                var staff = new User
                {
                    FirstName = "Dev",
                    LastName = role.ToString(),
                    Email = email,
                    Role = role,
                    TermsAcceptedAtUtc = DateTime.UtcNow
                };
                staff.PasswordHash = hasher.HashPassword(staff, "Staff@Sengen2026");
                db.Users.Add(staff);
            }

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
    }
}
