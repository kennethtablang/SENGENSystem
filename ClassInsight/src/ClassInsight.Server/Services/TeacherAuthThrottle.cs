using System.Collections.Concurrent;

namespace ClassInsight.Server.Services;

/// <summary>
/// Rate-limits teacher join-code attempts so the pairing code is brute-force resistant
/// even though it is short enough for a human to type. Failures are counted in a
/// sliding window and, once a threshold is crossed, further attempts are locked out
/// for a cooldown — enforced both per remote IP and globally (to blunt a distributed
/// guessing attempt across many connections).
/// </summary>
public sealed class TeacherAuthThrottle
{
    private const int MaxFailuresPerKey = 8;
    private const int GlobalMaxFailures = 30;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Lockout = TimeSpan.FromSeconds(30);

    private sealed class Counter
    {
        public int Failures;
        public DateTimeOffset WindowStart = DateTimeOffset.UtcNow;
        public DateTimeOffset? LockedUntil;
    }

    private readonly ConcurrentDictionary<string, Counter> _byKey = new();
    private readonly Counter _global = new();

    /// <summary>True if either this key or the whole server is currently locked out.</summary>
    public bool IsLockedOut(string key) => IsLockedOut(GetCounter(key)) || IsLockedOut(_global);

    public void RecordFailure(string key)
    {
        RecordFailure(GetCounter(key), MaxFailuresPerKey);
        RecordFailure(_global, GlobalMaxFailures);
    }

    /// <summary>Clears this key's failures on a successful registration.</summary>
    public void RecordSuccess(string key) => _byKey.TryRemove(key, out _);

    private Counter GetCounter(string key) => _byKey.GetOrAdd(key, _ => new Counter());

    private static bool IsLockedOut(Counter c)
    {
        lock (c)
        {
            if (c.LockedUntil is { } until)
            {
                if (DateTimeOffset.UtcNow < until) return true;
                c.LockedUntil = null;      // cooldown elapsed — reset
                c.Failures = 0;
                c.WindowStart = DateTimeOffset.UtcNow;
            }
            return false;
        }
    }

    private static void RecordFailure(Counter c, int threshold)
    {
        lock (c)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - c.WindowStart > Window)
            {
                c.WindowStart = now;
                c.Failures = 0;
            }
            c.Failures++;
            if (c.Failures >= threshold)
                c.LockedUntil = now + Lockout;
        }
    }
}
