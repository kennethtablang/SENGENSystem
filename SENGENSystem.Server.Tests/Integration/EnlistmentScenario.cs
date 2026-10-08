using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Tests.Integration
{
    /// <summary>
    /// Builds enlistment situations in the app's own database: subjects in the plan a year-1 ITP
    /// student resolves to, sections of them, published meetings at chosen times, and students who
    /// are genuinely eligible (linked account, confirmed, pre-authorized). Every code and number is
    /// tagged, so tests share one seeded database without seeing each other's rows.
    /// </summary>
    internal sealed class EnlistmentScenario(SengenAppFactory app)
    {
        public const string Registrar = "registrar@stialaminos.local";
        public const string StaffPassword = "Staff@Sengen2026";
        public const string StudentPassword = "Gate@Test2026";

        private static string Tag() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        /// <summary>The active term, opened for enlistment.</summary>
        public async Task<Semester> ActiveTermAsync()
        {
            await using var scope = app.Scope(out var db);
            var term = await db.Semesters.SingleAsync(s => s.IsActive);
            term.EnrollmentStage = EnrollmentStage.Enlistment;
            (await db.GetSettingsForUpdateAsync()).EnlistmentOpen = true;
            await db.SaveChangesAsync();
            return term;
        }

        /// <summary>A past, inactive term — for the cross-term (F-09) case.</summary>
        public async Task<Semester> PastTermAsync()
        {
            await using var scope = app.Scope(out var db);
            var term = new Semester
            {
                Name = $"Past term {Tag()}",
                StartDate = new DateOnly(2025, 8, 1),
                EndDate = new DateOnly(2025, 12, 15)
            };
            db.Semesters.Add(term);
            await db.SaveChangesAsync();
            return term;
        }

        /// <summary>Year-1 subjects in the curriculum the planner resolves for ITP, in <paramref name="term"/>'s term.</summary>
        public async Task<Subject[]> SubjectsAsync(Semester term, int count, bool secondRequiresFirst = false)
        {
            await using var scope = app.Scope(out var db);
            var curriculum = await db.Curricula
                .Where(c => !c.IsArchived && c.ProgramCode == nameof(ProgramTrack.ITP))
                .OrderByDescending(c => c.IsActive)
                .FirstOrDefaultAsync();
            if (curriculum is null)
            {
                curriculum = new Curriculum { ProgramCode = nameof(ProgramTrack.ITP), ProgramName = "ITP", IsActive = true };
                db.Curricula.Add(curriculum);
            }

            var tag = Tag();
            var subjects = Enumerable.Range(0, count).Select(i => new Subject
            {
                Curriculum = curriculum,
                ProgramCode = curriculum.ProgramCode,
                Code = $"S{i}{tag}",
                Title = $"Integration subject {i}",
                Units = 3,
                Hours = 3,
                LectureHours = 3,
                YearLevel = 1,
                Term = term.Term
            }).ToArray();
            db.Subjects.AddRange(subjects);
            if (secondRequiresFirst)
            {
                db.SubjectPrerequisites.Add(new SubjectPrerequisite { Subject = subjects[1], PrerequisiteSubject = subjects[0] });
            }
            await db.SaveChangesAsync();
            return subjects;
        }

        /// <summary>
        /// A section of <paramref name="subject"/> in <paramref name="term"/>, optionally with one
        /// published meeting at the given time (day, start and end in minutes from midnight).
        /// </summary>
        public async Task<Section> SectionAsync(
            Subject subject, Semester term, int capacity = 40,
            (DayOfWeek Day, int Start, int End)? publishedAt = null)
        {
            await using var scope = app.Scope(out var db);
            var section = new Section
            {
                SubjectId = subject.Id,
                SemesterId = term.Id,
                SectionCode = $"ITP-1Z-{subject.Code}-{Tag()}",
                ProgramCode = "ITP",
                YearLevel = 1,
                Block = "Z",
                Capacity = capacity
            };
            db.Sections.Add(section);

            if (publishedAt is { } at)
            {
                // Not an allowable grid slot — a synthetic period, as the board makes for free-form
                // placements — so it never appears as a choice anywhere else.
                var slot = new TimeSlot { Day = at.Day, StartMinutes = at.Start, EndMinutes = at.End, IsAllowable = false };
                db.TimeSlots.Add(slot);
                db.ScheduleAssignments.Add(new ScheduleAssignment
                {
                    SemesterId = term.Id,
                    Section = section,
                    TimeSlot = slot,
                    RoomId = await db.Rooms.Select(r => r.Id).FirstAsync(),
                    FacultyProfileId = await db.FacultyProfiles.Select(p => p.Id).FirstAsync(),
                    IsFinalized = true,
                    IsPublished = true
                });
            }
            await db.SaveChangesAsync();
            return section;
        }

        /// <summary>A confirmed, pre-authorized year-1 ITP student with a linked account.</summary>
        public async Task<(StudentRegistration Registration, string Email)> StudentAsync(Semester term)
        {
            var tag = Guid.NewGuid().ToString("N")[..8];
            var email = $"it.{tag}@example.test";
            var userId = await app.CreateStudentUserAsync(email, StudentPassword);

            await using var scope = app.Scope(out var db);
            var registration = new StudentRegistration
            {
                StudentNumber = $"IT-{tag}",
                FirstName = "INTEGRATION",
                LastName = $"TEST{tag.ToUpperInvariant()}",
                Email = email.ToUpperInvariant(),
                Program = ProgramTrack.ITP,
                StudentType = StudentType.NewStudent,
                Status = RegistrationStatus.Confirmed,
                IsPreAuthorized = true,
                YearLevel = 1,
                SemesterId = term.Id,
                UserId = userId,
                DateOfBirth = new DateOnly(2007, 5, 5)
            };
            db.StudentRegistrations.Add(registration);
            await db.SaveChangesAsync();
            return (registration, email);
        }

        public async Task RecordAsync(StudentRegistration student, Subject subject, Semester term, SubjectVerdict verdict)
        {
            await using var scope = app.Scope(out var db);
            db.StudentSubjectRecords.Add(new StudentSubjectRecord
            {
                StudentRegistrationId = student.Id,
                SubjectId = subject.Id,
                SemesterId = term.Id,
                Verdict = verdict
            });
            await db.SaveChangesAsync();
        }

        /// <summary>
        /// A slot request written straight to the database. With <see cref="SlotRequestStatus.Approved"/>
        /// it also takes the seat, as an approval would, so the section's count stays truthful.
        /// </summary>
        public async Task<Guid> RequestAsync(StudentRegistration student, Section section,
            SlotRequestStatus status = SlotRequestStatus.Requested)
        {
            await using var scope = app.Scope(out var db);
            var request = new SlotRequest { StudentRegistrationId = student.Id, SectionId = section.Id, Status = status };
            db.SlotRequests.Add(request);
            if (status == SlotRequestStatus.Approved)
            {
                (await db.Sections.SingleAsync(s => s.Id == section.Id)).EnrolledCount++;
            }
            await db.SaveChangesAsync();
            return request.Id;
        }

        public static async Task<string> MessageAsync(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
        }
    }
}
