namespace SENGENSystem.Server.Common
{
    /// <summary>
    /// The institution's local time, derived from UTC.
    ///
    /// <para>
    /// Domain timestamps are stored in UTC everywhere — <c>CreatedAtUtc</c>, <c>DecidedAtUtc</c>,
    /// <c>RecordedAtUtc</c>, the audit trail. The reports, however, called <c>DateTime.Now</c>
    /// directly in eight places across three files, which meant the "generated" stamp on a printed
    /// report came from <i>the server's locale</i> rather than the school's. On a developer machine
    /// that is invisible; on a host configured to UTC — the usual default for a container or a cloud
    /// VM — every report prints eight hours behind the office that generated it, and the room grid's
    /// "today" column highlights the wrong day.
    /// </para>
    ///
    /// <para>
    /// The rule this settles: <b>store UTC, convert once at the display edge.</b> That edge is here.
    /// Nothing else in the server should call <c>DateTime.Now</c>.
    /// </para>
    /// </summary>
    public static class InstitutionClock
    {
        /// <summary>
        /// Philippine Standard Time. Fixed rather than configurable because the institution is a
        /// single campus in one time zone — and because a wrong configured value fails silently in
        /// exactly the way this class exists to prevent. Made a constant so the assumption is
        /// greppable if that ever changes.
        /// </summary>
        public const string TimeZoneId = "Asia/Manila";

        private static readonly TimeZoneInfo Zone = ResolveZone();

        /// <summary>Now, in the institution's local time. The display-edge replacement for <c>DateTime.Now</c>.</summary>
        public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

        /// <summary>Converts any stored UTC timestamp to institution-local time for display.</summary>
        public static DateTime ToLocal(DateTime utc) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

        /// <summary>
        /// Windows and Linux disagree on time-zone ids — .NET resolves both on modern runtimes, but
        /// a stripped container image may carry no time-zone database at all. Falling back to UTC is
        /// the honest failure: a report an hour off is a nuisance, a report that throws on generation
        /// is an outage.
        /// </summary>
        private static TimeZoneInfo ResolveZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return TimeZoneInfo.Utc;
            }
        }
    }
}
