using SENGENSystem.Server.Common;

namespace SENGENSystem.Server.Tests
{
    /// <summary>
    /// The display edge. The bug being guarded against is silent and environment-dependent: the
    /// reports called <c>DateTime.Now</c>, which reads the *server's* locale, so on a UTC-configured
    /// host every printed report was stamped eight hours behind the office that generated it and the
    /// room grid highlighted the wrong day as "today". Nothing about that fails visibly in
    /// development, which is exactly why it needs a test.
    /// </summary>
    public class InstitutionClockTests
    {
        [Fact]
        public void Local_time_runs_ahead_of_utc_by_the_philippine_offset()
        {
            var utc = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc);

            var local = InstitutionClock.ToLocal(utc);

            // PST is UTC+8 year-round — the Philippines observes no daylight saving, which is why a
            // fixed expectation is safe here.
            Assert.Equal(new DateTime(2026, 8, 6, 8, 0, 0), local);
        }

        [Fact]
        public void The_offset_holds_across_a_date_boundary()
        {
            // The case the room grid's "today" column depends on: late-evening UTC is already the
            // next day locally, so a report generated at 5pm UTC must not highlight yesterday.
            var utc = new DateTime(2026, 8, 5, 17, 0, 0, DateTimeKind.Utc);

            var local = InstitutionClock.ToLocal(utc);

            Assert.Equal(new DateTime(2026, 8, 6, 1, 0, 0), local);
            Assert.Equal(DayOfWeek.Thursday, local.DayOfWeek);
        }

        [Fact]
        public void The_offset_does_not_shift_in_the_northern_summer()
        {
            // Guards against someone "helpfully" swapping the zone for one that observes DST.
            var january = InstitutionClock.ToLocal(new DateTime(2026, 1, 15, 4, 0, 0, DateTimeKind.Utc));
            var july = InstitutionClock.ToLocal(new DateTime(2026, 7, 15, 4, 0, 0, DateTimeKind.Utc));

            Assert.Equal(12, january.Hour);
            Assert.Equal(12, july.Hour);
        }

        [Fact]
        public void An_unspecified_kind_is_treated_as_utc_rather_than_local()
        {
            // Domain timestamps come back from EF with Kind=Unspecified. Reading those as local
            // would double-apply the offset — the subtlest version of the very bug being fixed.
            var unspecified = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Unspecified);

            Assert.Equal(new DateTime(2026, 8, 6, 8, 0, 0), InstitutionClock.ToLocal(unspecified));
        }

        [Fact]
        public void Now_tracks_utc_rather_than_the_machine_locale()
        {
            // The whole point: this must be right on a UTC-configured container, where DateTime.Now
            // and DateTime.UtcNow are identical and the old code was silently wrong.
            var expected = InstitutionClock.ToLocal(DateTime.UtcNow);

            Assert.True((InstitutionClock.Now - expected).Duration() < TimeSpan.FromSeconds(5));
        }
    }
}
