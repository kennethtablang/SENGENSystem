using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Tests
{
    /// <summary>
    /// A throwaway <see cref="AppDbContext"/> per test, plus the smallest world the enlistment rules
    /// need: a curriculum, its subjects, a term, and a student.
    ///
    /// <para>
    /// In-memory rather than a real database on purpose. What is under test here is <i>policy</i> —
    /// which subjects a student is owed, whether a prerequisite is met, what year their units earn —
    /// and none of that depends on SQL Server. The things that genuinely do (the
    /// <c>CK_Sections_EnrolledCount</c> check, the filtered unique index, the rowversion tokens) are
    /// not asserted here precisely because the in-memory provider would report success without
    /// enforcing them, which is worse than not testing them at all. Those need integration tests
    /// against a real database, and this file should not be mistaken for them.
    /// </para>
    /// </summary>
    internal sealed class TestDb : IDisposable
    {
        public AppDbContext Db { get; }

        public Semester PastTerm { get; private set; } = null!;
        public Semester ActiveTerm { get; private set; } = null!;
        public Curriculum Curriculum { get; private set; } = null!;
        public StudentRegistration Student { get; private set; } = null!;

        /// <summary>Year-1 first-semester subjects. <see cref="Advanced"/> requires <see cref="Basic"/>.</summary>
        public Subject Basic { get; private set; } = null!;
        public Subject Advanced { get; private set; } = null!;

        /// <summary>A year-1 subject with no prerequisite, for "is it in the plan?" assertions.</summary>
        public Subject Standalone { get; private set; } = null!;

        /// <summary>A year-2 subject, so year filtering has something to exclude.</summary>
        public Subject SecondYear { get; private set; } = null!;

        private TestDb()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                // A fresh store name per instance, so tests never see each other's rows however
                // they are ordered or parallelised.
                .UseInMemoryDatabase($"sengen-tests-{Guid.NewGuid()}")
                .Options;
            Db = new AppDbContext(options);
        }

        public static TestDb Create(StudentType studentType = StudentType.NewStudent, int yearLevel = 1)
        {
            var fixture = new TestDb();
            var db = fixture.Db;

            fixture.PastTerm = new Semester
            {
                Name = "AY 2025-2026 — First Semester",
                Term = SemesterTerm.FirstSemester,
                StartDate = new DateOnly(2025, 8, 1),
                EndDate = new DateOnly(2025, 12, 15)
            };
            fixture.ActiveTerm = new Semester
            {
                Name = "AY 2026-2027 — First Semester",
                Term = SemesterTerm.FirstSemester,
                IsActive = true,
                EnrollmentStage = EnrollmentStage.Enlistment,
                StartDate = new DateOnly(2026, 8, 1),
                EndDate = new DateOnly(2026, 12, 15)
            };
            db.Semesters.AddRange(fixture.PastTerm, fixture.ActiveTerm);

            fixture.Curriculum = new Curriculum
            {
                ProgramCode = nameof(ProgramTrack.ITP),
                ProgramName = "Information Technology Program",
                IsActive = true
            };
            db.Curricula.Add(fixture.Curriculum);

            fixture.Basic = NewSubject(fixture.Curriculum, "IT101", "Foundations", units: 3, year: 1);
            fixture.Advanced = NewSubject(fixture.Curriculum, "IT102", "Applications", units: 3, year: 1);
            fixture.Standalone = NewSubject(fixture.Curriculum, "GE101", "Communication", units: 3, year: 1);
            fixture.SecondYear = NewSubject(fixture.Curriculum, "IT201", "Systems", units: 3, year: 2);
            db.Subjects.AddRange(fixture.Basic, fixture.Advanced, fixture.Standalone, fixture.SecondYear);

            db.SubjectPrerequisites.Add(new SubjectPrerequisite
            {
                SubjectId = fixture.Advanced.Id,
                PrerequisiteSubjectId = fixture.Basic.Id
            });

            fixture.Student = new StudentRegistration
            {
                StudentNumber = "2026-000001",
                FirstName = "TEST",
                LastName = "STUDENT",
                Email = "test.student@example.com",
                Program = ProgramTrack.ITP,
                StudentType = studentType,
                Status = RegistrationStatus.Confirmed,
                YearLevel = yearLevel,
                SemesterId = fixture.ActiveTerm.Id
            };
            db.StudentRegistrations.Add(fixture.Student);

            db.SaveChanges();
            return fixture;
        }

        private static Subject NewSubject(Curriculum curriculum, string code, string title, int units, int year) =>
            new()
            {
                CurriculumId = curriculum.Id,
                ProgramCode = curriculum.ProgramCode,
                Code = code,
                Title = title,
                Units = units,
                YearLevel = year,
                Term = SemesterTerm.FirstSemester,
                Delivery = SubjectDelivery.LectureOnly,
                LectureHours = 3,
                Hours = 3
            };

        /// <summary>Records a verdict for the student, the way the Registrar's sheet would.</summary>
        public TestDb Record(Subject subject, SubjectVerdict verdict, Semester? term = null)
        {
            Db.StudentSubjectRecords.Add(new StudentSubjectRecord
            {
                StudentRegistrationId = Student.Id,
                SubjectId = subject.Id,
                SemesterId = (term ?? PastTerm).Id,
                Verdict = verdict
            });
            Db.SaveChanges();
            return this;
        }

        /// <summary>Completes a transferee evaluation crediting the given subjects.</summary>
        public TestDb CreditByEvaluation(params Subject[] subjects)
        {
            var evaluation = new TransfereeEvaluation
            {
                StudentRegistrationId = Student.Id,
                CurriculumId = Curriculum.Id,
                Status = TransfereeEvaluationStatus.Completed,
                Items = subjects.Select(s => new TransfereeEvaluationItem
                {
                    SubjectId = s.Id,
                    Decision = SubjectCreditDecision.Credited
                }).ToList()
            };
            Db.TransfereeEvaluations.Add(evaluation);
            Db.SaveChanges();
            return this;
        }

        public void Dispose() => Db.Dispose();
    }
}
