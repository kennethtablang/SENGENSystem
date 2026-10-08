namespace SENGENSystem.Server.Common.Reporting
{
    /// <summary>
    /// Subject colour coding for the exported grids, so a subject reads the same colour in a
    /// printed timetable as it does on the schedule board.
    /// <para>
    /// <b>The client is the source of truth</b> (<c>calendarUtils.js</c> — <c>SUBJECT_HUES</c> and
    /// <c>subjectHue</c>). This is its mirror, not a second design: the two cannot share code across
    /// languages, so <c>SubjectPaletteTests</c> pins this class to hues produced by running the
    /// client function, and either side drifting fails the build. Before this the copy lived as
    /// private helpers inside one report endpoint, kept in step by hand.
    /// </para>
    /// <para>
    /// The lightness differs from the screen on purpose — a printed fill needs a touch more
    /// colour than an on-screen tint to survive a laser printer — so only the <i>hue</i> is shared.
    /// </para>
    /// </summary>
    public static class SubjectPalette
    {
        private static readonly int[] Hues = { 214, 265, 330, 24, 43, 158, 190, 288, 8, 128, 300, 174 };

        /// <summary>The subject's hue, hashed from its id exactly as the client does.</summary>
        public static int HueFor(Guid subjectId)
        {
            uint h = 0;
            foreach (var ch in subjectId.ToString()) h = h * 31u + ch;
            return Hues[h % (uint)Hues.Length];
        }

        /// <summary>Light cell fill, as "RRGGBB".</summary>
        public static string FillHex(Guid subjectId) => HslToHex(HueFor(subjectId), 0.72, 0.90);

        /// <summary>Darker border in the same hue, as "RRGGBB".</summary>
        public static string BorderHex(Guid subjectId) => HslToHex(HueFor(subjectId), 0.55, 0.45);

        /// <summary>HSL (h in degrees, s/l in 0..1) to an "RRGGBB" hex string.</summary>
        internal static string HslToHex(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360;
            var c = (1 - Math.Abs(2 * l - 1)) * s;
            var x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
            var m = l - c / 2;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            int R = (int)Math.Round((r + m) * 255), G = (int)Math.Round((g + m) * 255), B = (int)Math.Round((b + m) * 255);
            return $"{R:X2}{G:X2}{B:X2}";
        }
    }
}
