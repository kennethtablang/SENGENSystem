using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Auth.Login
{
    /// <summary>
    /// The per-account half of the brute-force defence (the other half is the IP rate limiter
    /// registered in <c>Program.cs</c>). Failed sign-ins were audited as <c>LoginFailed</c> and
    /// otherwise ignored, so nothing anywhere slowed a password-guessing run down — the audit trail
    /// recorded the attack in detail while the API answered as fast as it could.
    ///
    /// <para>
    /// The numbers below are chosen against a human, not a benchmark. Five attempts is more than a
    /// person mistypes a password they know and far fewer than a dictionary needs; fifteen minutes
    /// is long enough to make guessing pointless (a 4-attempt-per-hour budget) and short enough that
    /// a locked-out staff member on a Monday morning waits rather than phoning the School Admin.
    /// </para>
    /// </summary>
    internal static class LoginThrottle
    {
        /// <summary>Consecutive failures that trigger a lockout.</summary>
        public const int MaxAttempts = 5;

        public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

        /// <summary>
        /// Records a failed attempt and locks the account once the cap is reached. Returns true when
        /// this failure is the one that locked it, so the caller can say so — and so the lockout is
        /// audited exactly once rather than on every subsequent attempt.
        /// </summary>
        public static bool RegisterFailure(User user, DateTime utcNow)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount < MaxAttempts) return false;

            user.LockedOutUntilUtc = utcNow.Add(LockoutDuration);
            // Reset the counter with the lockout, so the next window starts clean rather than
            // locking again on the first failure after it expires.
            user.FailedLoginCount = 0;
            return true;
        }

        /// <summary>Clears the failure state after a correct password.</summary>
        public static void RegisterSuccess(User user)
        {
            user.FailedLoginCount = 0;
            user.LockedOutUntilUtc = null;
        }

        /// <summary>
        /// What a locked-out user is told. Deliberately states the lockout plainly rather than
        /// reusing "Invalid email or password": the anti-enumeration argument does not apply once an
        /// attacker has already established the account exists by locking it, and hiding it from the
        /// legitimate owner would leave them retrying a correct password against a wall.
        /// </summary>
        public static string Message(DateTime lockedUntilUtc, DateTime utcNow)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((lockedUntilUtc - utcNow).TotalMinutes));
            return $"Too many failed sign-in attempts. This account is locked for {minutes} more "
                + $"minute{(minutes == 1 ? "" : "s")}. If this was not you, tell the School Admin — "
                + "the attempts are in the audit trail.";
        }
    }
}
