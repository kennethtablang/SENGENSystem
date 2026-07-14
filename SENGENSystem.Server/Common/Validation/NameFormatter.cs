using System.Globalization;

namespace SENGENSystem.Server.Common.Validation
{
    /// <summary>
    /// FR-AUTH-03 / FR-SIS-03: enforces proper name capitalization on all name inputs,
    /// addressing the documented apply.sti.edu gap.
    /// </summary>
    public static class NameFormatter
    {
        public static string ToProperCase(string name)
        {
            var collapsed = string.Join(' ', name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(collapsed.ToLowerInvariant());
        }
    }
}
