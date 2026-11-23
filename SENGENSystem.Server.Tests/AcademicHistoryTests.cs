using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.AcademicRecords;

namespace SENGENSystem.Server.Tests
{
    /// <summary>
    /// The rules behind F-10/F-11. These exist because live verification could reach the store and
    /// the derivations but not the prerequisite refusal itself — the seeded database had no
    /// published section for any subject carrying a prerequisite, and manufacturing one meant
    /// creating a faculty load, a cohort, a board placement, and a publish in a working environment.
    /// That is exactly the kind of thing a test should cover instead, so here it is covered.
    /// </summary>
    public class AcademicHistoryTests
    {
        [Fact]
        public async Task With_no_records_the_gate_falls_open()
        {
            // The single most consequential behaviour in the feature. If this ever flips, every
            // continuing student in a school that has not backfilled history is refused every
            // subject that has a prerequisite — an outage, not a rule.
            using var fixture = TestDb.Create();

            var history = await AcademicHistory.LoadAsync(fixture.Db, fixture.Student.Id, default);

            Assert.False(history.IsEnforceable);
            Assert.Empty(history.PassedSubjectIds);
            Assert.Empty(history.OwedSubjectIds);
            Assert.Equal(0, history.EarnedUnits);
        }

        [Fact]
        public async Task One_record_is_enough_to_start_enforcing()
        {
            using var fixture = TestDb.Create();
            fixture.Record(fixture.Standalone, SubjectVerdict.Passed);

            var history = await AcademicHistory.LoadAsync(fixture.Db, fixture.Student.Id, default);

            Assert.True(history.IsEnforceable);
        }

        [Fact]
        public async Task A_failed_subject_is_owed_and_earns_nothing()
        {
            using var fixture = TestDb.Create();
            fixture.Record(fixture.Basic, SubjectVerdict.Failed);

            var history = await AcademicHistory.LoadAsync(fixture.Db, fixture.Student.Id, default);

            Assert.Contains(fixture.Basic.Id, history.OwedSubjectIds);
            Assert.DoesNotContain(fixture.Basic.Id, history.PassedSubjectIds);
            Assert.Equal(0, history.EarnedUnits);
        }

        [Fact]
        public async Task A_dropped_subject_is_owed_exactly_like_a_failed_one()
        {
            // Dropped is kept distinct from Failed so the Registrar's record does not call a
            // withdrawal a failure — but everywhere it matters the two behave identically.
            using var fixture = TestDb.Create();
            fixture.Record(fixture.Basic, SubjectVerdict.Dropped);

            var history = await AcademicHistory.LoadAsync(fixture.Db, fixture.Student.Id, default);

            Assert.Contains(fixture.Basic.Id, history.OwedSubjectIds);
            Assert.Equal(0, history.EarnedUnits);
        }

        [Fact]
        public async Task A_retake_keeps_both_attempts_but_counts_its_units_once()
        {
            // Failed in one term, passed in the next. The subject is settled — not owed — and the
            // student has earned 3 units, not 6.
            using var fixture = TestDb.Create();
            fixture.Record(fixture.Basic, SubjectVerdict.Failed, fixture.PastTerm);
            fixture.Record(fixture.Basic, SubjectVerdict.Passed, fixture.ActiveTerm);

            var history = await AcademicHistory.LoadAsync(fixture.Db, fixture.Student.Id, default);

            Assert.Contains(fixture.Basic.Id, history.PassedSubjectIds);
            Assert.DoesNotContain(fixture.Basic.Id, history.OwedSubjectIds);
            Assert.Equal(3, history.EarnedUnits);
            Assert.Equal(2, history.RecordCount);
        }

        [Fact]
        public async Task An_unpassed_prerequisite_is_reported_as_unmet()
        {
            using var fixture = TestDb.Create();
            fixture.Record(fixture.Basic, SubjectVerdict.Failed);
            var history = await AcademicHistory.LoadAsync(fixture.Db, fixture.Student.Id, default);

            var unmet = await history.UnmetPrerequisitesAsync(fixture.Db, fixture.Advanced.Id, default);

            var subject = Assert.Single(unmet);
            Assert.Equal("IT101", subject.Code);
        }

        [Fact]
        public async Task A_passed_prerequisite_leaves_nothing_unmet()
        {
            using var fixture = TestDb.Create();
            fixture.Record(fixture.Basic, SubjectVerdict.Passed);
            var history = await AcademicHistory.LoadAsync(fixture.Db, fixture.Student.Id, default);

            var unmet = await history.UnmetPrerequisitesAsync(fixture.Db, fixture.Advanced.Id, default);

            Assert.Empty(unmet);
        }

        [Fact]
        public async Task A_transferees_credited_subject_satisfies_the_prerequisite_it_unlocks()
        {
            // The cruellest available bug, guarded: a transferee credited for the prerequisite,
            // refused the subject it unlocks, by the very evaluation meant to let them in.
            using var fixture = TestDb.Create(StudentType.Transferee);
            fixture.CreditByEvaluation(fixture.Basic);

            var history = await AcademicHistory.LoadAsync(fixture.Db, fixture.Student.Id, default);
            var unmet = await history.UnmetPrerequisitesAsync(fixture.Db, fixture.Advanced.Id, default);

            Assert.True(history.IsEnforceable);
            Assert.Contains(fixture.Basic.Id, history.PassedSubjectIds);
            Assert.Equal(3, history.EarnedUnits);
            Assert.Empty(unmet);
        }

        [Fact]
        public async Task A_subject_with_no_prerequisites_is_never_blocked()
        {
            using var fixture = TestDb.Create();
            fixture.Record(fixture.Basic, SubjectVerdict.Failed);
            var history = await AcademicHistory.LoadAsync(fixture.Db, fixture.Student.Id, default);

            var unmet = await history.UnmetPrerequisitesAsync(fixture.Db, fixture.Standalone.Id, default);

            Assert.Empty(unmet);
        }

        // ---- Co-requisites: same year level and term, so taken together ----
        // In the fixture IT102 requires IT101 and both are year 1, first semester, which makes the
        // pair a co-requisite (SubjectPrerequisite.IsCoRequisitePair) — the same reading the
        // schedule generator uses.

        [Fact]
        public async Task A_corequisite_held_this_term_is_met_without_having_been_passed()
        {
            // The bug: a continuing student (history on file) was refused IT102 because IT101 was
            // not *passed* — yet the curriculum schedules the two for the same term. Holding a seat
            // or a pending request in IT101 this term is what the pair asks for.
            using var fixture = TestDb.Create();
            fixture.Record(fixture.Standalone, SubjectVerdict.Passed); // makes the gate enforceable
            var history = await AcademicHistory.LoadAsync(fixture.Db, fixture.Student.Id, default);

            var unmet = await history.UnmetPrerequisitesAsync(
                fixture.Db, fixture.Advanced.Id, default, new HashSet<Guid> { fixture.Basic.Id });

            Assert.True(history.IsEnforceable);
            Assert.Empty(unmet);
        }

        [Fact]
        public async Task A_corequisite_not_held_this_term_is_still_unmet()
        {
            using var fixture = TestDb.Create();
            fixture.Record(fixture.Standalone, SubjectVerdict.Passed);
            var history = await AcademicHistory.LoadAsync(fixture.Db, fixture.Student.Id, default);

            var unmet = await history.UnmetPrerequisitesAsync(
                fixture.Db, fixture.Advanced.Id, default, new HashSet<Guid>());

            Assert.Equal("IT101", Assert.Single(unmet).Code);
        }

        [Fact]
        public async Task An_earlier_term_prerequisite_is_not_met_by_taking_it_now()
        {
            // IT201 (year 2) requires IT101 (year 1): a real prerequisite, not a co-requisite. A
            // student re-taking IT101 this term has still not passed it.
            using var fixture = TestDb.Create(yearLevel: 2);
            fixture.Db.SubjectPrerequisites.Add(new SubjectPrerequisite
            {
                SubjectId = fixture.SecondYear.Id,
                PrerequisiteSubjectId = fixture.Basic.Id
            });
            fixture.Db.SaveChanges();
            fixture.Record(fixture.Basic, SubjectVerdict.Failed);
            var history = await AcademicHistory.LoadAsync(fixture.Db, fixture.Student.Id, default);

            var unmet = await history.UnmetPrerequisitesAsync(
                fixture.Db, fixture.SecondYear.Id, default, new HashSet<Guid> { fixture.Basic.Id });

            Assert.Equal("IT101", Assert.Single(unmet).Code);
        }

        [Fact]
        public void An_unmet_corequisite_is_worded_as_take_it_together_not_pass_it_first()
        {
            using var fixture = TestDb.Create();

            var message = AcademicHistory.Refusal(
                fixture.Advanced.Code, [fixture.Basic], aboutSelf: true, fixture.Advanced);

            Assert.Contains("taken together with IT101", message);
            Assert.DoesNotContain("not passed", message);
        }
    }
}
