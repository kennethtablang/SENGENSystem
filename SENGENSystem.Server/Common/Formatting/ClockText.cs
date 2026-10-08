namespace SENGENSystem.Server.Common.Formatting
{
    /// <summary>
    /// The server's one formatter for class times and weekdays — every report, export, audit line
    /// and notice goes through it. Before this, "minutes from midnight → 08:30" was re-implemented
    /// privately in a dozen files, and the weekday abbreviation in two that already disagreed on
    /// Sunday ("Su" in one, "Sunday" in the other).
    /// <para>
    /// The client has its own formatter (<c>calendarUtils.js</c>) and cannot share this one — it is
    /// a different language. The goal is one documented source of truth <i>per side</i>, and the
    /// conventions below are written to match it: 24-hour "HH:mm" for data and audit, a 12-hour
    /// "h:mmAM" for the printed STI forms, and STI's M/T/W/Th/F/S weekday letters.
    /// </para>
    /// </summary>
    public static class ClockText
    {
        /// <summary>24-hour, zero-padded: 480 → "08:00", 810 → "13:30".</summary>
        public static string Hhmm(int minutes) => $"{minutes / 60:D2}:{minutes % 60:D2}";

        /// <summary>12-hour clock, no leading zero, as printed on STI forms: 480 → "8:00AM", 780 → "1:00PM".</summary>
        public static string H12(int minutes)
        {
            var h = minutes / 60;
            var m = minutes % 60;
            var suffix = h < 12 ? "AM" : "PM";
            var h12 = h % 12 == 0 ? 12 : h % 12;
            return $"{h12}:{m:00}{suffix}";
        }

        /// <summary>STI-style weekday letters: M, T, W, Th, F, S, Su.</summary>
        public static string DayAbbr(DayOfWeek day) => day switch
        {
            DayOfWeek.Monday => "M",
            DayOfWeek.Tuesday => "T",
            DayOfWeek.Wednesday => "W",
            DayOfWeek.Thursday => "Th",
            DayOfWeek.Friday => "F",
            DayOfWeek.Saturday => "S",
            _ => "Su"
        };
    }
}
