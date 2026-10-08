using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;

namespace SENGENSystem.Server.Features.Registration
{
    public sealed record LikelyDuplicateDto(Guid Id, string StudentNumber, string Status, string? SemesterName);

    /// <summary>
    /// F-03: the same <i>person</i> registered twice. Registration already refuses a second SIS on
    /// the same email; this catches the realistic paper-world duplicate — the same name and date of
    /// birth under a second mailbox — which the email check cannot see.
    /// <para>
    /// <b>It flags, it never blocks.</b> Two people can share a name and a birthday, and the SIS is
    /// an anonymous public form where refusing a real student is far worse than showing the
    /// Registrar a pair to compare. So the match is computed when the queue is read, not enforced
    /// when the form is submitted, and nothing is stored: there is no "duplicate" state to clear,
    /// keep in step, or get wrong after one of the two records is corrected.
    /// </para>
    /// <para>
    /// Names are stored in capitals (the SIS is ALL-CAPS), so an exact comparison is a
    /// case-insensitive one. Middle name and program are deliberately not part of the key — a
    /// typo'd middle name or a change of program between the two attempts is exactly how the second
    /// registration differs from the first.
    /// </para>
    /// </summary>
    internal static class LikelyDuplicates
    {
        public static async Task<Dictionary<Guid, List<LikelyDuplicateDto>>> FindAsync(
            AppDbContext db, IReadOnlyCollection<Guid> registrationIds, CancellationToken ct)
        {
            if (registrationIds.Count == 0) return [];

            var keys = await db.StudentRegistrations.AsNoTracking()
                .Where(r => registrationIds.Contains(r.Id))
                .Select(r => new { r.Id, r.LastName, r.FirstName, r.DateOfBirth })
                .ToListAsync(ct);

            var lastNames = keys.Select(k => k.LastName).Distinct().ToList();
            var candidates = await db.StudentRegistrations.AsNoTracking()
                .Where(r => lastNames.Contains(r.LastName))
                .Select(r => new
                {
                    r.Id, r.LastName, r.FirstName, r.DateOfBirth, r.StudentNumber, r.Status,
                    SemesterName = r.Semester != null ? r.Semester.Name : null
                })
                .ToListAsync(ct);

            var result = new Dictionary<Guid, List<LikelyDuplicateDto>>();
            foreach (var key in keys)
            {
                var matches = candidates
                    .Where(c => c.Id != key.Id
                        && c.LastName == key.LastName
                        && c.FirstName == key.FirstName
                        && c.DateOfBirth == key.DateOfBirth)
                    .Select(c => new LikelyDuplicateDto(c.Id, c.StudentNumber, c.Status.ToString(), c.SemesterName))
                    .ToList();
                if (matches.Count > 0) result[key.Id] = matches;
            }
            return result;
        }
    }
}
