using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Enlistment;
using SENGENSystem.Server.Features.Publishing.PublishSchedule;
using SENGENSystem.Server.Features.Registration;

namespace SENGENSystem.Server.Tests
{
    /// <summary>
    /// F-14 (the derived "enrolled" marker), F-03 (likely duplicates), and F-15 (publish requires
    /// finalize) — the three decisions from this pass that are policy rather than plumbing.
    /// </summary>
    public class EnrollmentCompletionTests
    {
        private static Section SectionFor(TestDb fixture, Subject subject, Semester term)
        {
            var section = new Section
            {
                SubjectId = subject.Id,
                SemesterId = term.Id,
                SectionCode = $"ITP-1A-{subject.Code}",
                ProgramCode = "ITP",
                YearLevel = 1,
                Block = "A"
            };
            fixture.Db.Sections.Add(section);
            fixture.Db.SaveChanges();
            return section;
        }

        private static void Approve(TestDb fixture, Section section, SlotRequestStatus status = SlotRequestStatus.Approved)
        {
            fixture.Db.SlotRequests.Add(new SlotRequest
            {
                StudentRegistrationId = fixture.Student.Id,
                SectionId = section.Id,
                Status = status
            });
            fixture.Db.SaveChanges();
        }

        private static Task<EnrollmentCompletionDto> Evaluate(TestDb fixture) =>
            EnrollmentCompletion.EvaluateAsync(fixture.Db, fixture.Student, fixture.ActiveTerm, default);

        [Fact]
        public async Task Nothing_approved_is_not_started()
        {
            using var fixture = TestDb.Create();

            var result = await Evaluate(fixture);

            Assert.Equal("NotStarted", result.State);
            Assert.Equal(3, result.PlannedSubjects);
            Assert.Equal(0, result.CoveredSubjects);
        }

        [Fact]
        public async Task Some_of_the_plan_approved_is_partial_and_names_what_is_missing()
        {
            using var fixture = TestDb.Create();
            Approve(fixture, SectionFor(fixture, fixture.Basic, fixture.ActiveTerm));

            var result = await Evaluate(fixture);

            Assert.Equal("Partial", result.State);
            Assert.Equal(1, result.CoveredSubjects);
            Assert.Equal(new[] { "GE101", "IT102" }, result.MissingCodes.OrderBy(c => c));
        }

        [Fact]
        public async Task Every_planned_subject_approved_is_enrolled()
        {
            using var fixture = TestDb.Create();
            foreach (var subject in new[] { fixture.Basic, fixture.Advanced, fixture.Standalone })
            {
                Approve(fixture, SectionFor(fixture, subject, fixture.ActiveTerm));
            }

            var result = await Evaluate(fixture);

            Assert.Equal("Enrolled", result.State);
            Assert.Empty(result.MissingCodes);
        }

        [Fact]
        public async Task A_pending_request_does_not_count_as_a_seat()
        {
            using var fixture = TestDb.Create();
            foreach (var subject in new[] { fixture.Basic, fixture.Advanced })
            {
                Approve(fixture, SectionFor(fixture, subject, fixture.ActiveTerm));
            }
            Approve(fixture, SectionFor(fixture, fixture.Standalone, fixture.ActiveTerm), SlotRequestStatus.Requested);

            Assert.Equal("Partial", (await Evaluate(fixture)).State);
        }

        [Fact]
        public async Task Last_terms_approval_does_not_make_this_term_enrolled()
        {
            // The cross-term shape of F-09: an approval for the same subject in an earlier term must
            // not satisfy this term's plan.
            using var fixture = TestDb.Create();
            foreach (var subject in new[] { fixture.Basic, fixture.Advanced, fixture.Standalone })
            {
                Approve(fixture, SectionFor(fixture, subject, fixture.PastTerm));
            }

            Assert.Equal("NotStarted", (await Evaluate(fixture)).State);
        }

        [Fact]
        public void An_empty_plan_is_not_enrolled()
        {
            // Nothing left to take is not the same as having enlisted for the term.
            var result = EnrollmentCompletion.Summarise([], new HashSet<Guid>());

            Assert.Equal("NotStarted", result.State);
            Assert.Equal(0, result.PlannedSubjects);
        }

        // ---- F-03: likely duplicates ----

        private static StudentRegistration Another(TestDb fixture, string first, string last, DateOnly dob)
        {
            var other = new StudentRegistration
            {
                StudentNumber = $"2026-{Random.Shared.Next(100000, 999999)}",
                FirstName = first,
                LastName = last,
                DateOfBirth = dob,
                Email = $"{Guid.NewGuid():N}@example.com",
                Program = ProgramTrack.ITP,
                SemesterId = fixture.ActiveTerm.Id
            };
            fixture.Db.StudentRegistrations.Add(other);
            fixture.Db.SaveChanges();
            return other;
        }

        [Fact]
        public async Task Same_name_and_birth_date_under_another_email_is_flagged()
        {
            using var fixture = TestDb.Create();
            fixture.Student.DateOfBirth = new DateOnly(2007, 3, 14);
            fixture.Db.SaveChanges();
            var twin = Another(fixture, "TEST", "STUDENT", new DateOnly(2007, 3, 14));

            var found = await LikelyDuplicates.FindAsync(fixture.Db, [fixture.Student.Id], default);

            var match = Assert.Single(found[fixture.Student.Id]);
            Assert.Equal(twin.Id, match.Id);
        }

        [Fact]
        public async Task A_different_birth_date_is_not_a_duplicate_and_a_record_never_matches_itself()
        {
            using var fixture = TestDb.Create();
            fixture.Student.DateOfBirth = new DateOnly(2007, 3, 14);
            fixture.Db.SaveChanges();
            Another(fixture, "TEST", "STUDENT", new DateOnly(2008, 3, 14));

            var found = await LikelyDuplicates.FindAsync(fixture.Db, [fixture.Student.Id], default);

            Assert.False(found.ContainsKey(fixture.Student.Id));
        }

        // ---- F-15: publish requires finalize ----

        [Fact]
        public void Publishing_is_blocked_while_any_draft_is_unfinalized()
        {
            Assert.NotNull(PublishScheduleEndpoint.BlockedReason(toPublish: 19, notFinalized: 1));
            Assert.Null(PublishScheduleEndpoint.BlockedReason(toPublish: 19, notFinalized: 0));
            // Nothing to publish is the idempotent no-op, not a refusal.
            Assert.Null(PublishScheduleEndpoint.BlockedReason(toPublish: 0, notFinalized: 0));
        }
    }
}
