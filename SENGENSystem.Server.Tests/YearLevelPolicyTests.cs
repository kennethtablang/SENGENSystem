using SENGENSystem.Server.Features.Registration;

namespace SENGENSystem.Server.Tests
{
    /// <summary>
    /// The ladder. Measured against the curriculum's own per-year totals rather than a fixed number,
    /// so a 60-unit-a-year program and a 42-unit-a-year one are each judged on their own terms.
    /// </summary>
    public class YearLevelPolicyTests
    {
        private static readonly Dictionary<int, int> Ladder = new()
        {
            [1] = 30,
            [2] = 30,
            [3] = 30,
            [4] = 30
        };

        [Theory]
        [InlineData(0, 1)]    // nothing earned — year 1
        [InlineData(29, 1)]   // one unit short of finishing year 1
        [InlineData(30, 2)]   // exactly year 1's load — now a second year
        [InlineData(59, 2)]
        [InlineData(60, 3)]
        [InlineData(90, 4)]
        [InlineData(200, 4)]  // beyond the ladder, clamped rather than running off it
        public void Earned_units_place_a_student_on_the_ladder(int earned, int expected) =>
            Assert.Equal(expected, YearLevelPolicy.FromEarnedUnits(earned, Ladder));

        [Fact]
        public void Credited_and_earned_units_are_the_same_currency()
        {
            // FromCreditedUnits is now a thin alias. If the two ever diverge, a transferee and a
            // continuing student with identical progress would land in different years.
            for (var units = 0; units <= 120; units += 15)
            {
                Assert.Equal(
                    YearLevelPolicy.FromEarnedUnits(units, Ladder),
                    YearLevelPolicy.FromCreditedUnits(units, Ladder));
            }
        }

        [Fact]
        public void A_gap_in_the_catalog_stops_promotion_rather_than_skipping_it()
        {
            // A year offering nothing cannot be "completed", so a student is not carried through it
            // on a technicality. Under-placing is the safe direction to be wrong in.
            var gapped = new Dictionary<int, int> { [1] = 30, [2] = 0, [3] = 30 };

            Assert.Equal(2, YearLevelPolicy.FromEarnedUnits(500, gapped));
        }

        [Fact]
        public void An_empty_ladder_leaves_everyone_at_year_one()
        {
            Assert.Equal(1, YearLevelPolicy.FromEarnedUnits(500, new Dictionary<int, int>()));
        }

        [Theory]
        [InlineData(1, true, 2)]
        [InlineData(2, false, 2)]   // same school year, second semester — still year 2
        [InlineData(4, true, 4)]    // final year stays at the top rather than running off the ladder
        public void The_calendar_rule_advances_only_when_the_school_year_turns_over(
            int current, bool newSchoolYear, int expected) =>
            Assert.Equal(expected, YearLevelPolicy.OnTermActivation(current, newSchoolYear));

        [Theory]
        [InlineData(0, 1)]
        [InlineData(5, 4)]
        public void Year_levels_are_clamped_onto_the_supported_ladder(int candidate, int expected) =>
            Assert.Equal(expected, YearLevelPolicy.Clamp(candidate));
    }
}
