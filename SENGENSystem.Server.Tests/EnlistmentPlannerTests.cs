using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Enlistment;

namespace SENGENSystem.Server.Tests
{
    /// <summary>
    /// What the student is offered. The regression that matters most here is the first one: adding
    /// academic history must not change the plan of a school that has recorded none, or the feature
    /// would have quietly rewritten every existing student's enlistment list on the day it shipped.
    /// </summary>
    public class EnlistmentPlannerTests
    {
        [Fact]
        public async Task With_no_history_the_plan_is_just_the_year_level_load()
        {
            using var fixture = TestDb.Create();

            var plan = await EnlistmentPlanner.ResolveAsync(
                fixture.Db, fixture.Student, fixture.ActiveTerm, default);

            Assert.True(plan.IsResolved);
            Assert.Equal(
                new[] { "GE101", "IT101", "IT102" },
                plan.Subjects.Select(s => s.Code).OrderBy(c => c));
            // The year-2 subject is not theirs yet.
            Assert.DoesNotContain(plan.Subjects, s => s.Code == "IT201");
            Assert.All(plan.Subjects, s => Assert.False(s.IsRepeat));
        }

        // ---- Curriculum revisions: a student keeps the catalog they entered under ----

        /// <summary>
        /// Two ITP curricula: the fixture's (effective AY 2025-2026, the past term's year, now
        /// inactive) and a revised one (effective AY 2026-2027, active) with its own year-1 subject.
        /// </summary>
        private static Curriculum ReviseCurriculum(TestDb fixture)
        {
            var db = fixture.Db;
            var oldYear = new SchoolYear { Name = "AY 2025-2026", StartDate = new DateOnly(2025, 6, 1), EndDate = new DateOnly(2026, 5, 31) };
            var newYear = new SchoolYear { Name = "AY 2026-2027", StartDate = new DateOnly(2026, 6, 1), EndDate = new DateOnly(2027, 5, 31), IsActive = true };
            db.SchoolYears.AddRange(oldYear, newYear);
            fixture.PastTerm.SchoolYearId = oldYear.Id;
            fixture.ActiveTerm.SchoolYearId = newYear.Id;

            fixture.Curriculum.IsActive = false;
            db.CurriculumSchoolYears.Add(new CurriculumSchoolYear { CurriculumId = fixture.Curriculum.Id, SchoolYearId = oldYear.Id });

            var revised = new Curriculum { ProgramCode = nameof(ProgramTrack.ITP), ProgramName = "ITP (revised)", IsActive = true };
            db.Curricula.Add(revised);
            db.CurriculumSchoolYears.Add(new CurriculumSchoolYear { CurriculumId = revised.Id, SchoolYearId = newYear.Id });
            db.Subjects.Add(new Subject
            {
                CurriculumId = revised.Id, ProgramCode = revised.ProgramCode, Code = "ITR101", Title = "Revised foundations",
                Units = 3, YearLevel = 1, Term = SemesterTerm.FirstSemester,
                Delivery = SubjectDelivery.LectureOnly, LectureHours = 3, Hours = 3
            });
            db.SaveChanges();
            return revised;
        }

        [Fact]
        public async Task A_continuing_student_keeps_the_curriculum_they_entered_under()
        {
            // The bug: activating a revised curriculum moved every continuing student onto it, where
            // none of their passed subjects exist. A student who entered in AY 2025-2026 stays on that
            // year's catalog.
            using var fixture = TestDb.Create();
            ReviseCurriculum(fixture);
            fixture.Student.SemesterId = fixture.PastTerm.Id; // entered last school year
            fixture.Db.SaveChanges();

            var plan = await EnlistmentPlanner.ResolveAsync(
                fixture.Db, fixture.Student, fixture.ActiveTerm, default);

            Assert.Equal(fixture.Curriculum.Id, plan.Curriculum?.Id);
            Assert.Contains(plan.Subjects, s => s.Code == "IT101");
            Assert.DoesNotContain(plan.Subjects, s => s.Code == "ITR101");
        }

        [Fact]
        public async Task A_new_student_gets_the_curriculum_in_force_for_their_entry_year()
        {
            using var fixture = TestDb.Create();
            var revised = ReviseCurriculum(fixture); // the student entered in the active term's year

            var plan = await EnlistmentPlanner.ResolveAsync(
                fixture.Db, fixture.Student, fixture.ActiveTerm, default);

            Assert.Equal(revised.Id, plan.Curriculum?.Id);
            Assert.Equal(new[] { "ITR101" }, plan.Subjects.Select(s => s.Code));
        }

        [Fact]
        public async Task A_passed_subject_drops_out_of_the_plan()
        {
            using var fixture = TestDb.Create();
            fixture.Record(fixture.Basic, SubjectVerdict.Passed);

            var plan = await EnlistmentPlanner.ResolveAsync(
                fixture.Db, fixture.Student, fixture.ActiveTerm, default);

            Assert.DoesNotContain(plan.Subjects, s => s.Code == "IT101");
            Assert.Contains(plan.Subjects, s => s.Code == "IT102");
        }

        [Fact]
        public async Task A_failed_subject_comes_back_flagged_as_a_repeat()
        {
            using var fixture = TestDb.Create();
            fixture.Record(fixture.Basic, SubjectVerdict.Failed);

            var plan = await EnlistmentPlanner.ResolveAsync(
                fixture.Db, fixture.Student, fixture.ActiveTerm, default);

            var repeat = Assert.Single(plan.Subjects, s => s.Code == "IT101");
            Assert.True(repeat.IsRepeat);
        }

        [Fact]
        public async Task A_failed_subject_from_an_earlier_year_is_offered_to_a_later_year_student()
        {
            // The case the plan could not express before: a second-year student still owing a
            // first-year subject was shown their own year's list and never the subject they owed,
            // so they could not enlist in their own repeat.
            using var fixture = TestDb.Create(yearLevel: 2);
            fixture.Record(fixture.Basic, SubjectVerdict.Failed);

            var plan = await EnlistmentPlanner.ResolveAsync(
                fixture.Db, fixture.Student, fixture.ActiveTerm, default);

            var repeat = Assert.Single(plan.Subjects, s => s.Code == "IT101");
            Assert.True(repeat.IsRepeat);
            Assert.True(repeat.IsBackSubject);
            // And their own year's load is still there.
            Assert.Contains(plan.Subjects, s => s.Code == "IT201");
        }

        [Fact]
        public async Task A_retaken_and_passed_subject_stops_coming_back()
        {
            // Without the "and not already passed" clause, a subject failed once would be offered
            // forever, even after it was cleared.
            using var fixture = TestDb.Create();
            fixture.Record(fixture.Basic, SubjectVerdict.Failed, fixture.PastTerm);
            fixture.Record(fixture.Basic, SubjectVerdict.Passed, fixture.ActiveTerm);

            var plan = await EnlistmentPlanner.ResolveAsync(
                fixture.Db, fixture.Student, fixture.ActiveTerm, default);

            Assert.DoesNotContain(plan.Subjects, s => s.Code == "IT101");
        }

        [Fact]
        public async Task A_transferees_credited_subject_is_not_offered()
        {
            using var fixture = TestDb.Create(StudentType.Transferee);
            fixture.CreditByEvaluation(fixture.Basic);

            var plan = await EnlistmentPlanner.ResolveAsync(
                fixture.Db, fixture.Student, fixture.ActiveTerm, default);

            Assert.DoesNotContain(plan.Subjects, s => s.Code == "IT101");
        }

        [Fact]
        public async Task With_no_registration_the_plan_is_empty_and_unresolved()
        {
            // Unresolved means the callers fall open rather than showing an empty page — a setup
            // gap must not read as "you have nothing to take".
            using var fixture = TestDb.Create();

            var plan = await EnlistmentPlanner.ResolveAsync(
                fixture.Db, null, fixture.ActiveTerm, default);

            Assert.False(plan.IsResolved);
            Assert.Empty(plan.Subjects);
        }
    }
}
