using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.AcademicRecords;

namespace SENGENSystem.Server.Features.Enlistment
{
    /// <summary>
    /// Keeps a co-requisite pair together after it is in (FR-ENL-16).
    /// <para>
    /// A dependent subject is let in because the student holds a seat or a pending request in its
    /// co-requisite this term (<see cref="AcademicHistory.UnmetPrerequisitesAsync"/>). That seat
    /// can be withdrawn later: cancelled or dropped by the student, rejected or dropped by the
    /// Registrar. Without this guard the student could keep the dependent and walk away from the
    /// co-requisite, which is a way around the gate. So withdrawing a co-requisite is refused while
    /// a dependent that relies on it is still held, and the refusal names that dependent. Withdraw
    /// the dependent first, then the co-requisite.
    /// </para>
    /// <para>
    /// It only applies where the gate itself applies: a student with history on file
    /// (<see cref="AcademicHistory.IsEnforceable"/>) who has not already passed the co-requisite.
    /// A passed co-requisite satisfies the dependent on its own, so withdrawing a re-take of it
    /// leaves nothing unmet.
    /// </para>
    /// </summary>
    internal static class CoRequisiteGuard
    {
        /// <summary>
        /// The subjects the student holds live this term that depend on <paramref name="subjectId"/>
        /// as a co-requisite. Empty means the subject may be withdrawn.
        /// </summary>
        public static async Task<List<Subject>> HeldDependentsAsync(
            AppDbContext db, Guid registrationId, Guid semesterId, Guid subjectId,
            CancellationToken cancellationToken)
        {
            var history = await AcademicHistory.LoadAsync(db, registrationId, cancellationToken);
            if (!history.IsEnforceable || history.HasPassed(subjectId)) return [];

            var edges = await db.SubjectPrerequisites.AsNoTracking()
                .Where(p => p.PrerequisiteSubjectId == subjectId)
                .Select(p => new { Dependent = p.Subject!, Prerequisite = p.PrerequisiteSubject! })
                .ToListAsync(cancellationToken);
            var dependents = edges
                .Where(e => SubjectPrerequisite.IsCoRequisitePair(e.Dependent, e.Prerequisite))
                .Select(e => e.Dependent)
                .ToList();
            if (dependents.Count == 0) return [];

            var dependentIds = dependents.Select(d => d.Id).ToList();
            var held = await db.SlotRequests.AsNoTracking()
                .Where(r => r.StudentRegistrationId == registrationId
                    && r.Section!.SemesterId == semesterId
                    && (r.Status == SlotRequestStatus.Requested || r.Status == SlotRequestStatus.Approved)
                    && dependentIds.Contains(r.Section.SubjectId))
                .Select(r => r.Section!.SubjectId)
                .ToListAsync(cancellationToken);

            return dependents
                .Where(d => held.Contains(d.Id))
                .OrderBy(d => d.Code)
                .ToList();
        }

        /// <summary>The refusal shown when <see cref="HeldDependentsAsync"/> is not empty.</summary>
        public static string Refusal(string subjectCode, IReadOnlyList<Subject> dependents, bool aboutSelf)
        {
            var list = string.Join(", ", dependents.Select(s => s.Code));
            return aboutSelf
                ? $"{subjectCode} is taken together with {list}, which you still hold this term. " +
                  $"Cancel or drop {list} first, then {subjectCode}."
                : $"{subjectCode} is taken together with {list}, which this student still holds this term. " +
                  $"Reject or drop {list} first, then {subjectCode}.";
        }
    }
}
