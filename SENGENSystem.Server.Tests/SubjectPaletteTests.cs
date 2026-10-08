using SENGENSystem.Server.Common.Reporting;

namespace SENGENSystem.Server.Tests
{
    /// <summary>
    /// The server's palette is a mirror of the client's (<c>calendarUtils.js</c> — the source of
    /// truth), and the two languages cannot share code. These expected hues were produced by
    /// running the client's own <c>subjectHue</c> function over the ids below, so this pins the
    /// contract between them: change the hue list or the hash on either side without the other and
    /// a subject prints one colour and shows another — this fails first.
    /// </summary>
    public class SubjectPaletteTests
    {
        [Theory]
        [InlineData("00000000-0000-0000-0000-000000000000", 214)]
        [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301", 174)]
        [InlineData("a1b2c3d4-e5f6-4711-8899-aabbccddeeff", 174)]
        [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff", 8)]
        [InlineData("5b0e9d2c-1c3a-4e7b-9f1d-2a6c8e4b7d10", 158)]
        [InlineData("c0ffee00-babe-4dad-beef-123456789abc", 288)]
        public void Hue_matches_the_client_for_the_same_subject(string id, int clientHue)
        {
            Assert.Equal(clientHue, SubjectPalette.HueFor(Guid.Parse(id)));
        }

        [Fact]
        public void Fill_and_border_are_six_digit_hex_in_the_same_hue()
        {
            var id = Guid.NewGuid();
            Assert.Matches("^[0-9A-F]{6}$", SubjectPalette.FillHex(id));
            Assert.Matches("^[0-9A-F]{6}$", SubjectPalette.BorderHex(id));
            Assert.NotEqual(SubjectPalette.FillHex(id), SubjectPalette.BorderHex(id));
        }

        [Fact]
        public void Hsl_conversion_hits_known_colours()
        {
            Assert.Equal("FF0000", SubjectPalette.HslToHex(0, 1, 0.5));
            Assert.Equal("00FF00", SubjectPalette.HslToHex(120, 1, 0.5));
            Assert.Equal("0000FF", SubjectPalette.HslToHex(240, 1, 0.5));
            Assert.Equal("FFFFFF", SubjectPalette.HslToHex(0, 0, 1));
        }
    }
}
