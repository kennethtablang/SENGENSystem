using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Registration;
using SENGENSystem.Server.Features.Registration.TransfereeEvaluation;

namespace SENGENSystem.Server.Features.Enlistment
{
    /// <summary>
    /// FR-ENL-01/06: the subjects one student still has to take in the term being enlisted for.
    /// Enlistment is not a catalog — a BSCS 2nd-year student browsing every published section of
    /// every program is being asked to know their own curriculum by heart, and one mis-click puts
    /// them in an HRA class nobody catches until the Registrar reviews it. This resolves the answer
    /// once, from the student's own record, and both the browse and the request leg work from it:
    /// the browser only shows these subjects, and a request for anything else is refused.
    /// <para>
    /// The rule, in one line: <b>their curriculum's subjects for their year level and this
    /// semester's term</b> — minus anything a transferee was credited, plus any back subject from
    /// an earlier year the Registrar explicitly ruled they must still take.
    /// </para>
    /// </summary>
    internal sealed record PlannedSubject(
        Guid SubjectId,
        string Code,
        string Title,
        int Units,
        int YearLevel,
        // True for a subject carried over from an earlier year (a transferee's ruled "to take").
        bool IsBackSubject);

    internal sealed record EnlistmentPlan(
        Domain.Curriculum? Curriculum,
        int YearLevel,
        SemesterTerm Term,
        IReadOnlyList<PlannedSubject> Subjects)
    {
        /// <summary>
        /// Whether the plan is authoritative enough to filter on. With no curriculum for the
        /// student's program there is no honest answer to "what do they take?", so the callers
        /// deliberately fall open — showing everything — rather than showing an empty page and
        /// blocking a student for a setup gap that is not theirs to fix.
        /// </summary>
        public bool IsResolved => Curriculum is not null;

        public IReadOnlySet<Guid> SubjectIds { get; } =
            Subjects.Select(s => s.SubjectId).ToHashSet();

        public string ProgramCode => Curriculum?.ProgramCode ?? string.Empty;

        public string TermLabel => Term == SemesterTerm.SecondSemester ? "Second Semester" : "First Semester";

        /// <summary>The empty plan: no student record, so nothing can be resolved.</summary>
        public static EnlistmentPlan None { get; } =
            new(null, YearLevelPolicy.EntryYearLevel, SemesterTerm.FirstSemester, []);
    }

    internal static class EnlistmentPlanner
    {
        /// <summary>
        /// Resolves what <paramref name="registration"/> still has to take in
        /// <paramref name="semester"/>. Reuses the transferee evaluation's own curriculum
        /// resolution so a student pinned to a specific curriculum during evaluation keeps it here.
        /// </summary>
        public static async Task<EnlistmentPlan> ResolveAsync(
            AppDbContext db,
            StudentRegistration? registration,
            Semester semester,
            CancellationToken cancellationToken)
        {
            if (registration is null) return EnlistmentPlan.None;

            var evaluation = registration.StudentType == StudentType.Transferee
                ? await TransfereeEvaluationEndpoints.LoadEvaluationAsync(
                    db, registration.Id, tracking: false, cancellationToken)
                : null;

            var curriculum = await ResolveCurriculumAsync(db, registration, evaluation, cancellationToken);
            if (curriculum is null)
            {
                return new EnlistmentPlan(null, registration.YearLevel, semester.Term, []);
            }

            // A completed evaluation is the only thing that moves subjects in or out of the plan.
            // An in-progress one is a draft the Registrar has not signed off, and acting on a draft
            // would drop subjects out from under a student mid-enlistment.
            IEnumerable<TransfereeEvaluationItem> ruled =
                evaluation is { Status: TransfereeEvaluationStatus.Completed } ? evaluation.Items : [];
            var credited = ruled
                .Where(i => i.Decision == SubjectCreditDecision.Credited)
                .Select(i => i.SubjectId)
                .ToHashSet();
            var toTake = ruled
                .Where(i => i.Decision == SubjectCreditDecision.ToTake)
                .Select(i => i.SubjectId)
                .ToHashSet();

            var yearLevel = YearLevelPolicy.Clamp(registration.YearLevel);

            var candidates = await db.Subjects.AsNoTracking()
                .Where(s => s.CurriculumId == curriculum.Id
                    && !s.IsArchived
                    && s.Term == semester.Term
                    && s.YearLevel <= yearLevel)
                .OrderBy(s => s.YearLevel).ThenBy(s => s.Code)
                .ToListAsync(cancellationToken);

            var subjects = candidates
                // Their own year's load, plus only those earlier-year subjects the Registrar ruled
                // they must still take. Without that second clause an evaluated transferee placed
                // in year 2 would never be offered the year-1 subject they were told to retake.
                .Where(s => (s.YearLevel == yearLevel || toTake.Contains(s.Id)) && !credited.Contains(s.Id))
                .Select(s => new PlannedSubject(s.Id, s.Code, s.Title, s.Units, s.YearLevel, s.YearLevel < yearLevel))
                .ToList();

            return new EnlistmentPlan(curriculum, yearLevel, semester.Term, subjects);
        }

        /// <summary>
        /// The curriculum this student's subjects come from: the one their evaluation was pinned to
        /// if it is still their program's, otherwise the active catalog for the program on their SIS
        /// (<see cref="StudentRegistration.Program"/> matched to <see cref="Domain.Curriculum.ProgramCode"/>,
        /// the same convention the transferee evaluation reads by).
        /// <para>
        /// Deliberately stricter than the evaluation's resolution, which falls back to <i>any</i>
        /// curriculum so a staff sheet is never blank. Here that fallback would be the exact bug
        /// this plan exists to prevent — quietly offering an HRA student the BSCS ladder. No match
        /// means no plan, and the caller falls open with a visible notice instead.
        /// </para>
        /// </summary>
        private static async Task<Domain.Curriculum?> ResolveCurriculumAsync(
            AppDbContext db,
            StudentRegistration registration,
            Domain.TransfereeEvaluation? evaluation,
            CancellationToken cancellationToken)
        {
            var program = registration.Program.ToString();

            if (evaluation?.CurriculumId is { } pinned)
            {
                var pinnedCurriculum = await db.Curricula.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == pinned, cancellationToken);
                if (pinnedCurriculum is not null && pinnedCurriculum.ProgramCode == program)
                {
                    return pinnedCurriculum;
                }
            }

            return await db.Curricula.AsNoTracking()
                .Where(c => !c.IsArchived && c.ProgramCode == program)
                .OrderByDescending(c => c.IsActive)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }
}
