using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Enlistment
{
    /// <summary>How far a student's term enlistment has got, judged against their own plan.</summary>
    public enum EnrollmentCompletionState
    {
        /// <summary>No curriculum resolves for the student, so there is no plan to be complete against.</summary>
        NoPlan,

        /// <summary>The plan has subjects and none of them hold an approved seat yet.</summary>
        NotStarted,

        /// <summary>Some, not all, of the plan's subjects hold an approved seat.</summary>
        Partial,

        /// <summary>Every subject the student still has to take this term holds an approved seat.</summary>
        Enrolled
    }

    public sealed record EnrollmentCompletionDto(
        string State,
        int PlannedSubjects,
        int CoveredSubjects,
        IReadOnlyList<string> MissingCodes);

    /// <summary>
    /// F-14: the term's per-student terminal state — "this student is enrolled". Before this the
    /// cycle's last stage was <i>Closed</i> but nothing said which students had actually finished,
    /// so "who is enrolled?" could only be answered by counting approvals by hand.
    /// <para>
    /// <b>Derived, never stored.</b> A student is enrolled when every subject their
    /// <see cref="EnlistmentPlanner">plan</see> says they still owe this term holds an approved
    /// seat. A stored flag would have to be kept in step with every approval, rejection, drop,
    /// re-evaluation and recorded verdict — each one a place for it to drift, the exact failure
    /// F-08 recorded for <c>EnrolledCount</c>. Computing it means it cannot disagree with the
    /// seats it summarises.
    /// </para>
    /// <para>
    /// An empty plan (everything already passed or credited) is not "enrolled": a student with
    /// nothing to take has nothing to complete, and calling them enrolled would let the marker
    /// vouch for a term they are not attending. It reads <see cref="EnrollmentCompletionState.NotStarted"/>
    /// with zero planned subjects, which the UI words as "no subjects to take".
    /// </para>
    /// </summary>
    internal static class EnrollmentCompletion
    {
        public static async Task<EnrollmentCompletionDto> EvaluateAsync(
            AppDbContext db, StudentRegistration registration, Semester semester, CancellationToken ct)
        {
            var plan = await EnlistmentPlanner.ResolveAsync(db, registration, semester, ct);
            if (!plan.IsResolved)
            {
                return new(nameof(EnrollmentCompletionState.NoPlan), 0, 0, []);
            }

            var approvedSubjectIds = (await db.SlotRequests.AsNoTracking()
                    .Where(r => r.StudentRegistrationId == registration.Id
                        && r.Status == SlotRequestStatus.Approved
                        && r.Section!.SemesterId == semester.Id)
                    .Select(r => r.Section!.SubjectId)
                    .ToListAsync(ct))
                .ToHashSet();

            return Summarise(plan.Subjects, approvedSubjectIds);
        }

        /// <summary>The pure half — split out so the rule can be tested without a database.</summary>
        internal static EnrollmentCompletionDto Summarise(
            IReadOnlyList<PlannedSubject> planned, IReadOnlySet<Guid> approvedSubjectIds)
        {
            var missing = planned.Where(s => !approvedSubjectIds.Contains(s.SubjectId)).ToList();
            var covered = planned.Count - missing.Count;

            var state = planned.Count == 0 || covered == 0
                ? EnrollmentCompletionState.NotStarted
                : missing.Count == 0
                    ? EnrollmentCompletionState.Enrolled
                    : EnrollmentCompletionState.Partial;

            return new(state.ToString(), planned.Count, covered, missing.Select(s => s.Code).ToList());
        }
    }
}
