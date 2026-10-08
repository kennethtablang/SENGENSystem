using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Enlistment;
using SENGENSystem.Server.Features.Enlistment.SeatCounts;
using SENGENSystem.Server.Features.Registration;

namespace SENGENSystem.Server.Tests.Integration
{
    /// <summary>
    /// The guarantees the seat lifecycle leans on, asserted where they actually live — in SQL
    /// Server. The approval and release code is careful, but its last line of defence is the
    /// database: if the CHECK, the rowversion, or the filtered index were ever dropped by a
    /// migration, every in-memory test would still pass. These would not.
    /// </summary>
    [Collection(SqlServerCollection.Name)]
    public class DatabaseConstraintTests(MigratedDatabase database)
    {
        /// <summary>A minimal, uniquely-coded world: one term, subject, section, and student.</summary>
        private sealed record World(Semester Term, Subject Subject, Section Section, StudentRegistration Student);

        private static async Task<World> CreateWorldAsync(AppDbContext db, int capacity = 2)
        {
            var tag = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            var term = new Semester
            {
                Name = $"Test term {tag}",
                StartDate = new DateOnly(2026, 8, 1),
                EndDate = new DateOnly(2026, 12, 15)
            };
            var subject = new Subject { Code = $"T{tag}", Title = "Test subject", Units = 3, Hours = 3, LectureHours = 3, ProgramCode = "ITP", YearLevel = 1 };
            var section = new Section
            {
                Subject = subject,
                Semester = term,
                SectionCode = $"ITP-1A-{tag}",
                ProgramCode = "ITP",
                YearLevel = 1,
                Block = "A",
                Capacity = capacity
            };
            var student = new StudentRegistration
            {
                StudentNumber = $"T-{tag}",
                FirstName = "TEST",
                LastName = $"STUDENT{tag}",
                Email = $"{tag}@EXAMPLE.TEST",
                Program = ProgramTrack.ITP,
                Status = RegistrationStatus.Confirmed,
                YearLevel = 1,
                Semester = term,
                DateOfBirth = new DateOnly(2007, 1, 1)
            };
            db.AddRange(term, subject, section, student);
            await db.SaveChangesAsync();
            return new World(term, subject, section, student);
        }

        [SqlFact]
        public async Task A_section_cannot_be_oversold_even_by_a_path_that_skips_the_application_check()
        {
            await using var db = database.NewContext();
            var world = await CreateWorldAsync(db, capacity: 2);

            world.Section.EnrolledCount = 3;
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("CK_Sections_EnrolledCount", ex.InnerException?.Message ?? ex.Message);
        }

        [SqlFact]
        public async Task A_seat_count_cannot_go_negative()
        {
            await using var db = database.NewContext();
            var world = await CreateWorldAsync(db);

            world.Section.EnrolledCount = -1;
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        [SqlFact]
        public async Task Two_racing_writers_on_one_section_cannot_both_win()
        {
            // The race the approval and release retry loops exist for. Without the rowversion the
            // second save would silently overwrite the first, and a seat would be lost or doubled.
            Guid sectionId;
            await using (var setup = database.NewContext())
            {
                sectionId = (await CreateWorldAsync(setup, capacity: 5)).Section.Id;
            }

            await using var first = database.NewContext();
            await using var second = database.NewContext();
            var a = await first.Sections.SingleAsync(s => s.Id == sectionId);
            var b = await second.Sections.SingleAsync(s => s.Id == sectionId);

            a.EnrolledCount++;
            await first.SaveChangesAsync();

            b.EnrolledCount++;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

            await using var check = database.NewContext();
            Assert.Equal(1, (await check.Sections.SingleAsync(s => s.Id == sectionId)).EnrolledCount);
        }

        [SqlFact]
        public async Task A_student_holds_at_most_one_live_request_per_section_but_may_request_again_after_dropping()
        {
            await using var db = database.NewContext();
            var world = await CreateWorldAsync(db);

            var first = new SlotRequest { StudentRegistrationId = world.Student.Id, SectionId = world.Section.Id };
            db.SlotRequests.Add(first);
            await db.SaveChangesAsync();

            db.SlotRequests.Add(new SlotRequest { StudentRegistrationId = world.Student.Id, SectionId = world.Section.Id });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            // Dropped falls outside the filter ([Status] IN ('Requested','Approved')), so the
            // subject can be requested again — the F-08 behaviour the filter was written for.
            var tracked = await db.SlotRequests.SingleAsync(r => r.Id == first.Id);
            tracked.Status = SlotRequestStatus.Dropped;
            await db.SaveChangesAsync();
            db.SlotRequests.Add(new SlotRequest { StudentRegistrationId = world.Student.Id, SectionId = world.Section.Id });
            await db.SaveChangesAsync();
        }

        // ---- Query translation: the class of bug the in-memory provider cannot see ----

        [SqlFact]
        public async Task The_seat_count_mismatch_query_translates_and_finds_drift()
        {
            await using var db = database.NewContext();
            var world = await CreateWorldAsync(db, capacity: 5);
            world.Section.EnrolledCount = 2; // drifted: no approved requests behind it
            await db.SaveChangesAsync();

            var mismatches = await SeatCountEndpoints.MismatchesAsync(db, world.Term.Id, default);

            var row = Assert.Single(mismatches);
            Assert.Equal(2, row.EnrolledCount);
            Assert.Equal(0, row.ApprovedRequests);
        }

        [SqlFact]
        public async Task The_enrolment_and_duplicate_queries_translate()
        {
            await using var db = database.NewContext();
            var world = await CreateWorldAsync(db);
            db.SlotRequests.Add(new SlotRequest
            {
                StudentRegistrationId = world.Student.Id,
                SectionId = world.Section.Id,
                Status = SlotRequestStatus.Approved
            });
            await db.SaveChangesAsync();

            // No curriculum for the program in this database: the plan is unresolved, and the
            // point here is that the queries behind it run on SQL Server at all.
            var completion = await EnrollmentCompletion.EvaluateAsync(db, world.Student, world.Term, default);
            Assert.Equal("NoPlan", completion.State);

            var duplicates = await LikelyDuplicates.FindAsync(db, [world.Student.Id], default);
            Assert.Empty(duplicates);
        }
    }
}
