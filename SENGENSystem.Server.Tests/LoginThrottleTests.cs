using SENGENSystem.Server.Domain;
using SENGENSystem.Server.Features.Auth.Login;

namespace SENGENSystem.Server.Tests
{
    /// <summary>The per-account half of the brute-force defence.</summary>
    public class LoginThrottleTests
    {
        private static User NewUser() => new() { Email = "staff@example.com" };

        [Fact]
        public void Failures_below_the_cap_do_not_lock_the_account()
        {
            var user = NewUser();
            var now = DateTime.UtcNow;

            for (var i = 1; i < LoginThrottle.MaxAttempts; i++)
            {
                Assert.False(LoginThrottle.RegisterFailure(user, now));
            }

            Assert.False(user.IsLockedOut(now));
        }

        [Fact]
        public void The_capping_failure_locks_the_account_and_reports_it_once()
        {
            var user = NewUser();
            var now = DateTime.UtcNow;

            for (var i = 1; i < LoginThrottle.MaxAttempts; i++)
            {
                LoginThrottle.RegisterFailure(user, now);
            }

            // Reported true exactly on the failure that locks, so the lockout is audited once
            // rather than on every subsequent attempt.
            Assert.True(LoginThrottle.RegisterFailure(user, now));
            Assert.True(user.IsLockedOut(now));
            Assert.Equal(now.Add(LoginThrottle.LockoutDuration), user.LockedOutUntilUtc);
        }

        [Fact]
        public void A_lockout_expires_rather_than_standing_forever()
        {
            // A permanent lockout would hand anyone who knows a staff email a denial-of-service
            // against that person — trading a hard attack for an easy one.
            var user = NewUser();
            var now = DateTime.UtcNow;
            for (var i = 0; i < LoginThrottle.MaxAttempts; i++)
            {
                LoginThrottle.RegisterFailure(user, now);
            }

            Assert.True(user.IsLockedOut(now));
            Assert.False(user.IsLockedOut(now.Add(LoginThrottle.LockoutDuration).AddSeconds(1)));
        }

        [Fact]
        public void The_next_window_starts_clean_after_a_lockout()
        {
            // Without resetting the counter alongside the lockout, the first failure after it
            // expired would lock the account again immediately.
            var user = NewUser();
            var now = DateTime.UtcNow;
            for (var i = 0; i < LoginThrottle.MaxAttempts; i++)
            {
                LoginThrottle.RegisterFailure(user, now);
            }

            Assert.Equal(0, user.FailedLoginCount);

            var later = now.Add(LoginThrottle.LockoutDuration).AddMinutes(1);
            Assert.False(LoginThrottle.RegisterFailure(user, later));
        }

        [Fact]
        public void A_correct_password_clears_the_streak()
        {
            // A typo before a correct entry must not carry over and lock the account some later day.
            var user = NewUser();
            var now = DateTime.UtcNow;
            LoginThrottle.RegisterFailure(user, now);
            LoginThrottle.RegisterFailure(user, now);

            LoginThrottle.RegisterSuccess(user);

            Assert.Equal(0, user.FailedLoginCount);
            Assert.Null(user.LockedOutUntilUtc);
            Assert.False(user.IsLockedOut(now));
        }

        [Fact]
        public void The_message_states_the_remaining_time_and_never_says_zero_minutes()
        {
            var now = DateTime.UtcNow;

            Assert.Contains("15 more minutes", LoginThrottle.Message(now.AddMinutes(15), now));
            // Rounds up rather than telling someone to wait "0 minutes" while still locked.
            Assert.Contains("1 more minute", LoginThrottle.Message(now.AddSeconds(5), now));
        }
    }
}
