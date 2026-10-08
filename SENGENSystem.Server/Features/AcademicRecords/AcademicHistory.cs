using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Registration;

namespace SENGENSystem.Server.Features.AcademicRecords
{
    /// <summary>
    /// What one student has already earned — the single answer every downstream rule asks, resolved
    /// once so the prerequisite gate, the enlistment plan, and the year-level ladder cannot disagree
    /// about the same student.
    ///
    /// <para>
    /// Two sources feed it, and both have to, because a transferee's credits are academic history
    /// that simply happened somewhere else: the <see cref="StudentSubjectRecord"/> rows for subjects
    /// taken here, and the <see cref="SubjectCreditDecision.Credited"/> items of a <b>completed</b>
    /// transferee evaluation. Leaving the second one out would have been the cruellest possible bug
    /// — a transferee credited for the prerequisite would be refused the subject it unlocks, by the
    /// very evaluation that was supposed to let them in.
    /// </para>
    /// </summary>
    internal sealed record AcademicHistory(
        IReadOnlySet<Guid> PassedSubjectIds,
        IReadOnlySet<Guid> OwedSubjectIds,
        int EarnedUnits,
        int RecordCount,
        bool HasCreditEvaluation)
    {
        /// <summary>
        /// Whether there is enough on file to judge this student by.
        ///
        /// <para>
        /// This is the most consequential line in the file, so it is worth stating plainly: with no
        /// history recorded, <b>every</b> prerequisite reads as unmet. Enforcing against that would
        /// not be strictness, it would be a system-wide outage dressed as a rule — every continuing
        /// student in the school refused every subject that has a prerequisite, on the first day the
        /// gate shipped, because nobody had backfilled a term of records yet.
        /// </para>
        ///
        /// <para>
        /// So the gate falls <b>open</b> for a student with nothing on file and closes the moment
        /// there is something to check against. That is the same discipline
        /// <c>EnlistmentPlan.IsResolved</c> already follows for a missing curriculum: a setup gap is
        /// not the student's fault and must not read as a locked door. The trade is deliberate and
        /// it is stated in the refusal message and the audit entry, so a student who *is* blocked
        /// can be told exactly which record said so.
        /// </para>
        /// </summary>
        public bool IsEnforceable => RecordCount > 0 || HasCreditEvaluation;

        public bool HasPassed(Guid subjectId) => PassedSubjectIds.Contains(subjectId);

        public static AcademicHistory Empty { get; } =
            new(new HashSet<Guid>(), new HashSet<Guid>(), 0, 0, false);

        /// <summary>
        /// Loads the history of one student. A subject counts as passed if <i>any</i> attempt at it
        /// passed — there is no need to order the attempts, because passing is not reversible and a
        /// later failure of an already-passed subject is not a thing the Registrar records.
        /// Conversely a subject is still owed when it has attempts and none of them passed.
        /// </summary>
        public static async Task<AcademicHistory> LoadAsync(
            AppDbContext db, Guid registrationId, CancellationToken cancellationToken)
        {
            var records = await db.StudentSubjectRecords.AsNoTracking()
                .Where(r => r.StudentRegistrationId == registrationId)
                .Select(r => new { r.SubjectId, r.Verdict, Units = r.Subject!.Units })
                .ToListAsync(cancellationToken);

            var credited = await db.TransfereeEvaluations.AsNoTracking()
                .Where(e => e.StudentRegistrationId == registrationId
                    && e.Status == TransfereeEvaluationStatus.Completed)
                .SelectMany(e => e.Items)
                .Where(i => i.Decision == SubjectCreditDecision.Credited)
                .Select(i => new { i.SubjectId, Units = i.Subject!.Units })
                .ToListAsync(cancellationToken);

            var passed = records
                .Where(r => r.Verdict.EarnsCredit())
                .Select(r => r.SubjectId)
                .Concat(credited.Select(c => c.SubjectId))
                .ToHashSet();

            // Owed = attempted and never passed. Excluding anything in `passed` matters for the
            // retake case: a subject failed in one term and passed in the next is settled, and
            // must not keep coming back into the plan forever.
            var owed = records
                .Where(r => r.Verdict.StillOwed())
                .Select(r => r.SubjectId)
                .Where(id => !passed.Contains(id))
                .ToHashSet();

            // Units are counted per distinct subject, not per record. A student who failed a
            // 3-unit subject and then passed it has earned 3 units, not 6.
            var unitsBySubject = records
                .Where(r => r.Verdict.EarnsCredit())
                .Select(r => (r.SubjectId, r.Units))
                .Concat(credited.Select(c => (c.SubjectId, c.Units)))
                .GroupBy(x => x.SubjectId)
                .ToDictionary(g => g.Key, g => g.First().Units);

            return new AcademicHistory(
                passed,
                owed,
                unitsBySubject.Values.Sum(),
                records.Count,
                credited.Count > 0);
        }

        /// <summary>
        /// The prerequisites of <paramref name="subjectId"/> this student has not passed, in the
        /// order they print on the prospectus. Empty when the subject has none, when they are all
        /// met, or when there is no history to judge by — callers check
        /// <see cref="IsEnforceable"/> for the last case rather than reading an empty list as "met".
        /// </summary>
        public async Task<List<Subject>> UnmetPrerequisitesAsync(
            AppDbContext db, Guid subjectId, CancellationToken cancellationToken)
        {
            var prerequisites = await db.SubjectPrerequisites.AsNoTracking()
                .Where(p => p.SubjectId == subjectId)
                .Select(p => p.PrerequisiteSubject!)
                .OrderBy(s => s.YearLevel).ThenBy(s => s.Code)
                .ToListAsync(cancellationToken);

            return prerequisites.Where(p => !HasPassed(p.Id)).ToList();
        }

        /// <summary>
        /// The year level this student's earned units place them in, or <c>null</c> when there is
        /// nothing to derive it from — no history on file, or no curriculum for their program.
        ///
        /// <para>
        /// A null is not a failure, it is the honest answer, and the caller falls back to the
        /// calendar rule (<see cref="YearLevelPolicy.OnTermActivation"/>). The two differ in exactly
        /// the case worth caring about: a student who failed enough of last year to still owe it
        /// stays where they are here, and is promoted anyway by the calendar. Neither is imposed —
        /// both are recommendations the Admission Officer can override on validation, which is the
        /// same standing the transferee derivation has always had.
        /// </para>
        /// </summary>
        public async Task<int?> DeriveYearLevelAsync(
            AppDbContext db, StudentRegistration registration, CancellationToken cancellationToken)
        {
            if (!IsEnforceable) return null;

            var program = registration.Program.ToString();
            var curriculum = await db.Curricula.AsNoTracking()
                .Where(c => !c.IsArchived && c.ProgramCode == program)
                .OrderByDescending(c => c.IsActive)
                .FirstOrDefaultAsync(cancellationToken);
            if (curriculum is null) return null;

            var unitsByYear = await db.Subjects.AsNoTracking()
                .Where(s => s.CurriculumId == curriculum.Id && !s.IsArchived)
                .GroupBy(s => s.YearLevel)
                .Select(g => new { Year = g.Key, Units = g.Sum(s => s.Units) })
                .ToDictionaryAsync(x => x.Year, x => x.Units, cancellationToken);
            if (unitsByYear.Count == 0) return null;

            return YearLevelPolicy.FromEarnedUnits(EarnedUnits, unitsByYear);
        }

        /// <summary>
        /// The refusal a caller shows when <see cref="UnmetPrerequisitesAsync"/> comes back
        /// non-empty. Written once here because the request leg and the approval leg must say the
        /// same thing about the same student — and because the sentence has to name the subjects
        /// and point at the remedy, since a student cannot fix a missing record themselves.
        /// </summary>
        public static string Refusal(string subjectCode, IReadOnlyList<Subject> unmet, bool aboutSelf)
        {
            var list = string.Join(", ", unmet.Select(s => s.Code));
            var subject = aboutSelf ? "You have" : "This student has";
            var remedy = aboutSelf
                ? "If you have already taken " + (unmet.Count == 1 ? "it" : "them") +
                  ", see the Registrar — your academic record may not be complete."
                : "If their record is incomplete, add the missing subjects under Academic records first.";
            return $"{subject} not passed the prerequisite" + (unmet.Count == 1 ? "" : "s") +
                   $" for {subjectCode}: {list}. {remedy}";
        }
    }
}
