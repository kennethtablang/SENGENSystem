using Microsoft.EntityFrameworkCore;
using SENGENSystem.Server.Common.Persistence;
using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Registration
{
    /// <summary>
    /// The curriculum a student is held to: the one in force for their program in the school year
    /// they <b>entered</b>, not whichever one is active today.
    /// <para>
    /// A curriculum revision applies to the incoming batch. A student already part-way through
    /// keeps the catalog they started on. Reading "the active curriculum" instead moved every
    /// continuing student to the new catalog the moment it was activated. Their passed subjects
    /// are rows of the old catalog, so the new one read them as never taken: they were offered
    /// subjects they had already passed and refused others for "unmet" prerequisites.
    /// </para>
    /// <para>
    /// The entry year is the school year of <see cref="StudentRegistration.SemesterId"/>, the term
    /// the SIS was filed for. A term activation never changes it. The curriculum is the one whose
    /// effectivity (<see cref="CurriculumSchoolYear"/>) covers that year; the curriculum endpoints
    /// already guarantee at most one per program and year. A transferee's evaluation pins its own
    /// curriculum, and the callers check that pin before asking here.
    /// </para>
    /// <para>
    /// When no curriculum is linked to the entry year (records from before effectivity existed,
    /// or a term with no school year), this falls back to the program's active curriculum. That is
    /// the previous behaviour, so nothing that worked before changes.
    /// </para>
    /// </summary>
    internal static class StudentCurriculum
    {
        public static async Task<Domain.Curriculum?> ResolveAsync(
            AppDbContext db, StudentRegistration registration, CancellationToken cancellationToken)
        {
            var program = registration.Program.ToString();

            var entryYearId = await db.Semesters.AsNoTracking()
                .Where(s => s.Id == registration.SemesterId)
                .Select(s => s.SchoolYearId)
                .FirstOrDefaultAsync(cancellationToken);

            if (entryYearId is { } yearId)
            {
                var entryCurriculum = await db.Curricula.AsNoTracking()
                    .Where(c => !c.IsArchived
                        && c.ProgramCode == program
                        && c.SchoolYears.Any(y => y.SchoolYearId == yearId))
                    .OrderByDescending(c => c.IsActive)
                    .FirstOrDefaultAsync(cancellationToken);
                if (entryCurriculum is not null) return entryCurriculum;
            }

            return await db.Curricula.AsNoTracking()
                .Where(c => !c.IsArchived && c.ProgramCode == program)
                .OrderByDescending(c => c.IsActive)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }
}
