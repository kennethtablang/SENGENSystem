using SENGENSystem.Server.Domain;

namespace SENGENSystem.Server.Features.Documents.Reminders
{
    /// <summary>
    /// How often an enrollee may be chased, and how much of the queue one sweep may take.
    ///
    /// <para>
    /// The sweep previously had neither limit: it loaded every matching registration and emailed
    /// each one inline, so a large term blocked (and could time out) the Registrar's button with no
    /// indication of how far it had got, and pressing it twice emailed the same students twice. The
    /// second failure is the one students actually notice — being reminded four times in a morning
    /// about a paper they already handed in reads as the system being broken, and it trains people
    /// to ignore the mail that matters.
    /// </para>
    /// </summary>
    internal static class ReminderPolicy
    {
        /// <summary>
        /// The quiet period between blanket reminders to the same enrollee. A day is long enough
        /// that a student is never chased twice about the same paperwork in one sitting, and short
        /// enough that a genuine daily follow-up during the closing week still works.
        /// </summary>
        public static readonly TimeSpan MinimumInterval = TimeSpan.FromHours(20);

        /// <summary>
        /// Rows one sweep will queue. Beyond this the officer is told there is more to do and can
        /// press again — a bounded, resumable sweep with an honest count beats an unbounded one that
        /// may or may not have finished.
        /// </summary>
        public const int BatchCap = 200;

        /// <summary>Whether a blanket sweep may chase this enrollee now.</summary>
        public static bool MayRemind(StudentRegistration registration, DateTime utcNow) =>
            registration.LastRemindedAtUtc is not { } last || utcNow - last >= MinimumInterval;

        /// <summary>
        /// The idempotency key for one enrollee's reminder. Scoped to the day so a second sweep in
        /// the same day cannot queue a duplicate even if the interval check were somehow bypassed —
        /// belt and braces, because a duplicate email cannot be recalled.
        /// </summary>
        public static string DedupeKey(StudentRegistration registration, DateTime utcNow) =>
            $"reminder:{registration.Id}:{utcNow:yyyyMMdd}";
    }
}
