using SENGENSystem.Server.Common.Formatting;
using SENGENSystem.Server.Features.Registration;

namespace SENGENSystem.Server.Tests
{
    /// <summary>
    /// The two text conventions that used to be re-implemented per file: the server's clock text
    /// (a dozen private copies, two of which disagreed on Sunday) and the SIS's ALL-CAPS rule
    /// (which a staff correction used to break by writing Proper Case).
    /// </summary>
    public class FormattingTests
    {
        [Theory]
        [InlineData(0, "00:00")]
        [InlineData(480, "08:00")]
        [InlineData(810, "13:30")]
        [InlineData(1439, "23:59")]
        public void Hhmm_is_zero_padded_24_hour(int minutes, string expected) =>
            Assert.Equal(expected, ClockText.Hhmm(minutes));

        [Theory]
        [InlineData(0, "12:00AM")]
        [InlineData(480, "8:00AM")]
        [InlineData(720, "12:00PM")]
        [InlineData(780, "1:00PM")]
        [InlineData(1050, "5:30PM")]
        public void H12_matches_the_printed_sti_forms(int minutes, string expected) =>
            Assert.Equal(expected, ClockText.H12(minutes));

        [Theory]
        [InlineData(DayOfWeek.Monday, "M")]
        [InlineData(DayOfWeek.Thursday, "Th")]
        [InlineData(DayOfWeek.Saturday, "S")]
        // The two old copies disagreed here ("Su" vs "Sunday"); one answer now.
        [InlineData(DayOfWeek.Sunday, "Su")]
        public void Day_abbreviations_use_sti_letters(DayOfWeek day, string expected) =>
            Assert.Equal(expected, ClockText.DayAbbr(day));

        [Theory]
        [InlineData("dela cruz", "DELA CRUZ")]
        [InlineData("  juan   carlos ", "JUAN CARLOS")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void Sis_text_is_trimmed_collapsed_capitals(string? input, string expected) =>
            Assert.Equal(expected, SisText.Caps(input));
    }
}
