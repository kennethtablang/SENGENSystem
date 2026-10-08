namespace SENGENSystem.Server.Features.Registration
{
    /// <summary>
    /// The SIS's text convention in one place: stored ALL-CAPS, with runs of spaces collapsed, the
    /// way the paper form is filled in. Every path that writes SIS text — the public form, a staff
    /// correction, a student's own correction — goes through this, so a record cannot change case
    /// depending on who last touched it.
    /// </summary>
    internal static class SisText
    {
        public static string Caps(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    }
}
