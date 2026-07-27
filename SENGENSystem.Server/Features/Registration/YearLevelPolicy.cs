using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Registration
{
    /// <summary>
    /// How SEN-GEN decides what year a student is in (FR-SIS-09). Nobody types a year level on the
    /// SIS, because for three of the four cases the answer is already implied by something the
    /// system knows — and the fourth is a judgement call that belongs to the Registrar, not a form
    /// field. The rules, in the order they apply:
    /// <list type="bullet">
    ///   <item>A <b>new enrollee</b> starts at year 1. There is nothing to derive.</item>
    ///   <item>A <b>transferee</b> gets the year their credited units earn, computed from the
    ///     evaluation the Registrar completes (<see cref="FromCreditedUnits"/>).</item>
    ///   <item>A <b>returning student</b> moves up one year when their term activation for a new
    ///     school year is validated — and stays put within the same school year, because the
    ///     second semester of year 2 is still year 2.</item>
    ///   <item>Any of them can be <b>overridden</b> by staff; the derivation is a recommendation,
    ///     and the override is audited.</item>
    /// </list>
    /// </summary>
    internal static class YearLevelPolicy
    {
        /// <summary>The curriculum ladder SEN-GEN supports. Beyond this a student is graduating.</summary>
        public const int MinYearLevel = 1;
        public const int MaxYearLevel = 4;

        /// <summary>Where every student who has no other evidence starts.</summary>
        public const int EntryYearLevel = 1;

        /// <summary>Clamps any candidate year level onto the supported ladder.</summary>
        public static int Clamp(int yearLevel) => Math.Clamp(yearLevel, MinYearLevel, MaxYearLevel);

        public static bool IsValid(int yearLevel) => yearLevel is >= MinYearLevel and <= MaxYearLevel;

        /// <summary>
        /// The year level a fresh registration enters at. A transferee's real level is not known
        /// until the Registrar evaluates them, so they too start at 1 and are moved by the
        /// evaluation — an unevaluated transferee is never silently placed in a higher year.
        /// </summary>
        public static int ForNewRegistration(StudentType studentType) => EntryYearLevel;

        /// <summary>
        /// The year a transferee's credited units earn them. Measured against the curriculum's own
        /// per-year unit totals rather than a fixed number, so a 60-unit-a-year program and a
        /// 42-unit-a-year program are each judged on their own terms: a student is in year N once
        /// they have been credited everything years 1..N-1 ask for.
        /// <para>
        /// <paramref name="unitsByYear"/> maps year level → total units that year offers. Years
        /// missing from it are treated as carrying no units, which lands the student at year 1 —
        /// the safe direction to be wrong in, since it under-places rather than over-places.
        /// </para>
        /// </summary>
        public static int FromCreditedUnits(int creditedUnits, IReadOnlyDictionary<int, int> unitsByYear)
        {
            var year = MinYearLevel;
            var cumulative = 0;
            for (var candidate = MinYearLevel; candidate < MaxYearLevel; candidate++)
            {
                var yearUnits = unitsByYear.GetValueOrDefault(candidate);
                // A year that offers nothing can't be "completed" — stop rather than promote the
                // student through a gap in the catalog.
                if (yearUnits <= 0) break;
                cumulative += yearUnits;
                if (creditedUnits < cumulative) break;
                year = candidate + 1;
            }
            return Clamp(year);
        }

        /// <summary>
        /// The year a returning student moves into when activated for a term. Advancing turns on
        /// the <i>school year</i> changing, not the semester: a student activating for the second
        /// semester of the year they are already in stays in that year. A final-year student stays
        /// at the top of the ladder rather than running off it.
        /// </summary>
        public static int OnTermActivation(int currentYearLevel, bool isNewSchoolYear) =>
            Clamp(isNewSchoolYear ? currentYearLevel + 1 : currentYearLevel);

        /// <summary>Human-readable ordinal for messages and reports: 1 → "1st year".</summary>
        public static string Label(int yearLevel) => yearLevel switch
        {
            1 => "1st year",
            2 => "2nd year",
            3 => "3rd year",
            4 => "4th year",
            _ => $"Year {yearLevel}"
        };
    }
}
