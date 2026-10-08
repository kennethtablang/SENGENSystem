using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Documents.Reminders;

namespace SENGENSystem.Server.Tests
{
    /// <summary>
    /// The quiet period and the dedupe key behind F-07. The failure being guarded against is not
    /// abstract: with no memory at all, pressing "Send reminders" twice emailed every incomplete
    /// student twice, and nothing stopped an officer doing it daily.
    /// </summary>
    public class ReminderPolicyTests
    {
        private static StudentRegistration NewRegistration(DateTime? lastReminded = null) =>
            new() { LastRemindedAtUtc = lastReminded };

        [Fact]
        public void Someone_never_reminded_may_always_be_chased()
        {
            Assert.True(ReminderPolicy.MayRemind(NewRegistration(), DateTime.UtcNow));
        }

        [Fact]
        public void A_second_sweep_straight_away_is_refused()
        {
            var now = DateTime.UtcNow;
            var registration = NewRegistration(lastReminded: now);

            Assert.False(ReminderPolicy.MayRemind(registration, now));
            Assert.False(ReminderPolicy.MayRemind(registration, now.AddHours(1)));
        }

        [Fact]
        public void The_quiet_period_expires()
        {
            var now = DateTime.UtcNow;
            var registration = NewRegistration(lastReminded: now);

            Assert.True(ReminderPolicy.MayRemind(
                registration, now.Add(ReminderPolicy.MinimumInterval)));
        }

        [Fact]
        public void The_interval_leaves_room_for_a_genuine_daily_follow_up()
        {
            // 20 hours rather than a flat 24, deliberately: an officer who sweeps each morning
            // should not find the window still closed because yesterday's run was a little later.
            var now = DateTime.UtcNow;
            var registration = NewRegistration(lastReminded: now);

            Assert.True(ReminderPolicy.MayRemind(registration, now.AddHours(24)));
            Assert.True(ReminderPolicy.MinimumInterval < TimeSpan.FromHours(24));
        }

        [Fact]
        public void The_dedupe_key_is_stable_within_a_day_and_distinct_across_students()
        {
            var a = NewRegistration();
            var b = NewRegistration();
            var morning = new DateTime(2026, 8, 5, 8, 0, 0, DateTimeKind.Utc);
            var evening = new DateTime(2026, 8, 5, 20, 0, 0, DateTimeKind.Utc);

            Assert.Equal(ReminderPolicy.DedupeKey(a, morning), ReminderPolicy.DedupeKey(a, evening));
            Assert.NotEqual(ReminderPolicy.DedupeKey(a, morning), ReminderPolicy.DedupeKey(b, morning));
        }

        [Fact]
        public void The_dedupe_key_rolls_over_to_the_next_day()
        {
            var registration = NewRegistration();
            var today = new DateTime(2026, 8, 5, 23, 0, 0, DateTimeKind.Utc);

            Assert.NotEqual(
                ReminderPolicy.DedupeKey(registration, today),
                ReminderPolicy.DedupeKey(registration, today.AddDays(1)));
        }

        [Fact]
        public void The_batch_cap_is_bounded_and_positive()
        {
            // A guard rather than a tautology: a cap of 0 would silently disable reminders entirely,
            // and an unbounded one is the original bug.
            Assert.InRange(ReminderPolicy.BatchCap, 1, 1000);
        }
    }
}
