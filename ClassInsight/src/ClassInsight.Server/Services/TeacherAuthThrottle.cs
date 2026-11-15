using System.Collections.Concurrent;

namespace ClassInsight.Server.Services;

/// <summary>
/// Fail-closed brute-force protection for teacher pairing (FR-SEC-06). Two layers:
///
///  * <b>Per source</b> (client IP): after a few free attempts each further failure doubles a
///    lockout, capped, so one device can't hammer the code.
///  * <b>Global</b>: if failures across <i>all</i> sources exceed a budget inside a sliding
///    window, every new attempt is refused for a cool-down — so an attacker rotating source
///    addresses still can't make fast progress.
///
/// Combined with a ≥64-bit generated code (see <see cref="ServerIdentity"/>), online guessing is
/// infeasible. Open mode never consults this.
/// </summary>
public sealed class TeacherAuthThrottle
{
    // Per-source policy.
    public const int FreeAttempts = 5;
    private static readonly TimeSpan BaseLockout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxLockout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DecayWindow = TimeSpan.FromMinutes(10);

    // Global policy.
    public const int GlobalBudget = 30;
    private static readonly TimeSpan GlobalWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan GlobalCooldown = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _clock;

    public TeacherAuthThrottle() : this(TimeProvider.System) { }

    public TeacherAuthThrottle(TimeProvider clock) => _clock = clock;

    private sealed class Record
    {
        public int Failures;
        public DateTimeOffset LockedUntil;
        public DateTimeOffset LastAttempt;
    }

    private readonly ConcurrentDictionary<string, Record> _records = new();

    private readonly object _globalLock = new();
    private readonly Queue<DateTimeOffset> _globalFailures = new();
    private DateTimeOffset _globalLockedUntil;

    /// <summary>
    /// If attempts from <paramref name="key"/> are currently refused (per-source or global
    /// lockout), returns the remaining wait; otherwise null. Call before validating a code.
    /// </summary>
    public TimeSpan? GetLockout(string key)
    {
        var now = _clock.GetUtcNow();
        TimeSpan? wait = null;

        lock (_globalLock)
        {
            if (_globalLockedUntil > now) wait = _globalLockedUntil - now;
        }

        if (_records.TryGetValue(key, out var rec))
        {
            lock (rec)
            {
                if (rec.LockedUntil > now)
                {
                    var local = rec.LockedUntil - now;
                    if (wait is null || local > wait) wait = local;
                }
            }
        }

        return wait;
    }

    /// <summary>Record a failed attempt and (re)compute both lockouts.</summary>
    public void RegisterFailure(string key)
    {
        var now = _clock.GetUtcNow();

        var rec = _records.GetOrAdd(key, _ => new Record());
        lock (rec)
        {
            // Let old failures decay so a transient fat-finger doesn't lock a device for long.
            if (now - rec.LastAttempt > DecayWindow) rec.Failures = 0;

            rec.Failures++;
            rec.LastAttempt = now;

            if (rec.Failures > FreeAttempts)
            {
                var over = rec.Failures - FreeAttempts;
                var backoff = TimeSpan.FromTicks(Math.Min(
                    BaseLockout.Ticks * (1L << Math.Min(over - 1, 20)),
                    MaxLockout.Ticks));
                rec.LockedUntil = now + backoff;
            }
        }

        lock (_globalLock)
        {
            _globalFailures.Enqueue(now);
            while (_globalFailures.Count > 0 && now - _globalFailures.Peek() > GlobalWindow)
                _globalFailures.Dequeue();

            if (_globalFailures.Count >= GlobalBudget)
            {
                _globalLockedUntil = now + GlobalCooldown;
                _globalFailures.Clear();
            }
        }

        // Keep the per-source table from growing without bound under a spray of addresses.
        if (_records.Count > 4096)
            foreach (var (k, r) in _records)
                if (now - r.LastAttempt > DecayWindow && r.LockedUntil <= now)
                    _records.TryRemove(k, out _);
    }

    /// <summary>Clear the source's record on a successful registration.</summary>
    public void RegisterSuccess(string key) => _records.TryRemove(key, out _);
}
