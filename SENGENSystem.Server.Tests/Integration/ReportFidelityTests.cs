using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Reports;
using SENGENSystem.Server.Features.Reports.FacultyLoading;

namespace SENGENSystem.Server.Tests.Integration
{
    /// <summary>
    /// The three places the Confirmation of Faculty Loading used to approximate STI's form —
    /// a proxy "Class No.", the Academic Head standing in as Program Head, and laboratory hours
    /// credited hour for hour — and the report aggregates now computed in SQL.
    /// </summary>
    [Collection(AppCollection.Name)]
    public class ReportFidelityTests(SengenAppFactory app)
    {
        private const string Admin = "admin@stialaminos.local";
        private const string AdminPassword = "Admin@Sengen2026";

        private static string Tag() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        private sealed record Arranged(Guid Semester, Guid FacultyProfileId, Guid FacultyUserId, string FacultyName,
            Section Section, Guid LabRoomId);

        /// <summary>
        /// A faculty member of their own (so totals are theirs alone) teaching one lecture-laboratory
        /// subject: a 3-hour lecture in a lecture room and a 3-hour laboratory in a computer lab.
        /// </summary>
        private async Task<Arranged> ArrangeAsync()
        {
            var term = await new EnlistmentScenario(app).ActiveTermAsync();
            var tag = Tag();

            await using var scope = app.Scope(out var db);
            var user = new User
            {
                FirstName = "Report",
                LastName = $"Fidelity{tag}",
                Email = $"fidelity{tag.ToLowerInvariant()}@test.local",
                PasswordHash = "x",
                Role = UserRole.FacultyMember
            };
            var faculty = new FacultyProfile { User = user, ProgramCode = "ITP", EmployeeId = $"F{tag}" };
            var curriculumId = await db.Curricula.Where(c => c.ProgramCode == "ITP").Select(c => c.Id).FirstAsync();
            var subject = new Subject
            {
                CurriculumId = curriculumId, ProgramCode = "ITP", Code = $"LL{tag}", Title = "Lec-lab subject",
                Units = 3, Delivery = SubjectDelivery.LectureLaboratory, LectureHours = 3, LaboratoryHours = 3, Hours = 6,
                LabRoomKind = RoomKind.ComputerLaboratory, YearLevel = 1, Term = term.Term
            };
            var cohort = new ClassSection { SemesterId = term.Id, ProgramCode = "ITP", YearLevel = 1, SectionName = $"F{tag}" };
            var section = new Section
            {
                Subject = subject, SemesterId = term.Id, SectionCode = $"ITP-1F{tag}-{subject.Code}",
                ProgramCode = "ITP", YearLevel = 1, Block = cohort.SectionName, Capacity = 40
            };
            db.AddRange(user, faculty, subject, cohort, section);
            db.FacultyLoadAssignments.Add(new FacultyLoadAssignment
            {
                FacultyProfile = faculty, Subject = subject, ClassSection = cohort, SemesterId = term.Id
            });

            var lectureRoom = await db.Rooms.Where(r => r.Kind == RoomKind.LectureRoom).OrderBy(r => r.Name).Select(r => r.Id).FirstAsync();
            var labRoom = await db.Rooms.Where(r => r.Kind == RoomKind.ComputerLaboratory).OrderBy(r => r.Name).Select(r => r.Id).FirstAsync();
            foreach (var (day, room) in new[] { (DayOfWeek.Monday, lectureRoom), (DayOfWeek.Thursday, labRoom) })
            {
                db.ScheduleAssignments.Add(new ScheduleAssignment
                {
                    SemesterId = term.Id,
                    Section = section,
                    TimeSlot = new TimeSlot { Day = day, StartMinutes = 7 * 60, EndMinutes = 10 * 60, IsAllowable = false },
                    RoomId = room,
                    FacultyProfile = faculty
                });
            }
            await db.SaveChangesAsync();
            return new Arranged(term.Id, faculty.Id, user.Id, user.FullName, section, labRoom);
        }

        private async Task SetLabFactorAsync(double factor)
        {
            await using var scope = app.Scope(out var db);
            var settings = await db.SystemSettings.SingleAsync();
            settings.LabTeachingCreditFactor = factor;
            await db.SaveChangesAsync();
        }

        private async Task<FacultyLoadReportDto> MemoAsync(Arranged a)
        {
            await using var scope = app.Scope(out var db);
            var term = await db.Semesters.SingleAsync(s => s.Id == a.Semester);
            return (await FacultyLoadingPdfData.BuildAsync(term, db, a.FacultyProfileId, CancellationToken.None)).Single();
        }

        [SqlFact]
        public async Task Every_section_gets_its_own_class_number_and_the_memo_prints_it()
        {
            var a = await ArrangeAsync();
            var other = await ArrangeAsync();

            Assert.True(a.Section.ClassNumber >= 1001, $"minted {a.Section.ClassNumber}");
            Assert.NotEqual(a.Section.ClassNumber, other.Section.ClassNumber);

            var memo = await MemoAsync(a);
            Assert.Equal(a.Section.ClassNumber.ToString(), memo.Lines.First(l => l.ShowSubjectInfo).ClassNo);
        }

        [SqlFact]
        public async Task Laboratory_hours_are_credited_at_the_institution_factor()
        {
            var a = await ArrangeAsync();
            try
            {
                await SetLabFactorAsync(1.0);
                var even = await MemoAsync(a);
                Assert.Equal(6, even.TotalContactHours, 3);
                Assert.Equal(6, even.TotalTeachingHours, 3);

                await SetLabFactorAsync(0.75);
                var memo = await MemoAsync(a);
                Assert.Equal(6, memo.TotalContactHours, 3);
                Assert.Equal(5.25, memo.TotalTeachingHours, 3);   // 3 lecture + 3 × 0.75 laboratory
                Assert.Equal(["LEC", "LAB"], memo.Lines.Select(l => l.Type).ToArray());
                Assert.Equal(2.25, memo.Lines.Single(l => l.Type == "LAB").TeachingHours, 3);
            }
            finally
            {
                await SetLabFactorAsync(SystemSettings.DefaultLabTeachingCreditFactor);
            }
        }

        [SqlFact]
        public async Task The_lab_factor_is_set_on_system_parameters_and_bounded()
        {
            var admin = await app.SignedInAsync(Admin, AdminPassword);
            try
            {
                var tooLow = await admin.PutAsJsonAsync("/api/parameters/settings", new { labTeachingCreditFactor = 0.1 });
                Assert.Equal(HttpStatusCode.BadRequest, tooLow.StatusCode);
                var tooHigh = await admin.PutAsJsonAsync("/api/parameters/settings", new { labTeachingCreditFactor = 1.5 });
                Assert.Equal(HttpStatusCode.BadRequest, tooHigh.StatusCode);

                var ok = await admin.PutAsJsonAsync("/api/parameters/settings", new { labTeachingCreditFactor = 0.75 });
                Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

                var page = await admin.GetFromJsonAsync<JsonElement>("/api/parameters");
                Assert.Equal(0.75, page.GetProperty("facultyLoading").GetProperty("labTeachingCreditFactor").GetDouble(), 3);
            }
            finally
            {
                await SetLabFactorAsync(SystemSettings.DefaultLabTeachingCreditFactor);
            }
        }

        [SqlFact]
        public async Task The_designated_program_head_signs_thru_and_clearing_falls_back_to_the_academic_head()
        {
            var a = await ArrangeAsync();
            var admin = await app.SignedInAsync(Admin, AdminPassword);
            try
            {
                var set = await admin.PutAsJsonAsync("/api/parameters/program-heads/ITP", new { userId = a.FacultyUserId });
                Assert.Equal(HttpStatusCode.OK, set.StatusCode);

                var page = await admin.GetFromJsonAsync<JsonElement>("/api/parameters");
                var itp = page.GetProperty("programHeads").GetProperty("programs").EnumerateArray()
                    .Single(p => p.GetProperty("programCode").GetString() == "ITP");
                Assert.Equal(a.FacultyUserId, itp.GetProperty("headUserId").GetGuid());

                await using (var scope = app.Scope(out var db))
                {
                    var sig = await FacultyLoadingSignatories.LoadAsync(db, CancellationToken.None);
                    Assert.Equal(a.FacultyName, sig.ProgramHeadFor("ITP"));
                    Assert.Equal(sig.AcademicHead, sig.ProgramHeadFor("HRA"));   // no head designated
                }

                // A second head for the same program replaces the first rather than colliding.
                var b = await ArrangeAsync();
                var replace = await admin.PutAsJsonAsync("/api/parameters/program-heads/ITP", new { userId = b.FacultyUserId });
                Assert.Equal(HttpStatusCode.OK, replace.StatusCode);

                // A student cannot be designated; an unknown program is a 404.
                var studentId = await app.CreateStudentUserAsync($"ph{Tag().ToLowerInvariant()}@test.local", "Student@Test2026");
                var student = await admin.PutAsJsonAsync("/api/parameters/program-heads/ITP", new { userId = studentId });
                Assert.Equal(HttpStatusCode.BadRequest, student.StatusCode);
                var unknown = await admin.PutAsJsonAsync("/api/parameters/program-heads/NOPE", new { userId = b.FacultyUserId });
                Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

                var clear = await admin.PutAsJsonAsync("/api/parameters/program-heads/ITP", new { userId = (Guid?)null });
                Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
                await using (var scope = app.Scope(out var db))
                {
                    var sig = await FacultyLoadingSignatories.LoadAsync(db, CancellationToken.None);
                    Assert.Equal(sig.AcademicHead, sig.ProgramHeadFor("ITP"));
                }
            }
            finally
            {
                await using var scope = app.Scope(out var db);
                await db.Users.Where(u => u.ProgramHeadOf != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.ProgramHeadOf, (string?)null));
            }
        }

        [SqlFact]
        public async Task Report_aggregates_computed_in_sql_match_the_meetings()
        {
            var a = await ArrangeAsync();
            await using var scope = app.Scope(out var db);
            var term = await db.Semesters.SingleAsync(s => s.Id == a.Semester);

            var summary = (await FacultyLoadingReportsEndpoints.BuildRowsAsync(term, db, CancellationToken.None))
                .Single(r => r.FacultyProfileId == a.FacultyProfileId);
            Assert.Equal(3, summary.TotalUnits);
            Assert.Equal(1, summary.TotalSubjects);
            Assert.Equal(6, summary.ScheduledHours, 3);

            // The laboratory meeting runs 07:00–10:00: three hours, two of them inside the 08:00–17:00 window.
            var (_, rooms) = await ReportsEndpoints.RoomUtilizationRowsAsync(term, db, CancellationToken.None);
            var labName = await db.Rooms.Where(r => r.Id == a.LabRoomId).Select(r => r.Name).SingleAsync();
            var expectedHours = await db.ScheduleAssignments
                .Where(m => m.SemesterId == term.Id && m.RoomId == a.LabRoomId)
                .Select(m => new { m.TimeSlot!.StartMinutes, m.TimeSlot.EndMinutes })
                .ToListAsync();
            var row = rooms.Single(r => (string)r[0] == labName);
            Assert.Equal(expectedHours.Count, (int)row[4]);
            Assert.Equal(Math.Round(expectedHours.Sum(m => m.EndMinutes - m.StartMinutes) / 60.0, 1), (double)row[5], 3);
            Assert.Equal(Math.Round(expectedHours.Sum(m =>
                Math.Max(0, Math.Min(m.EndMinutes, 17 * 60) - Math.Max(m.StartMinutes, 8 * 60))) / 60.0, 1), (double)row[6], 3);
        }
    }
}
