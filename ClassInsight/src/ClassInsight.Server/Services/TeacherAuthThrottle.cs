using System.Collections.Concurrent;

namespace ClassInsight.Server.Services;

/// <summary>
/// Fail-closed brute-force protection for teacher pairing (FR-SEC-02, NFR-SEC-03). Tracks
/// failed join-code attempts per source key (client IP) and imposes an escalating lockout
/// so a wrong code can't be guessed by rapid retries. Open mode never consults this.
///
/// Reconstructed from the hub's usage and the plan's security NFRs after the original was lost.
/// </summary>
public sealed class TeacherAuthThrottle
{
    private const int FreeAttempts = 5;              // attempts before lockout kicks in
    private static readonly TimeSpan BaseLockout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxLockout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DecayWindow = TimeSpan.FromMinutes(10);

    private sealed class Record
    {
        public int Failures;
        public DateTimeOffset LockedUntil;
        public DateTimeOffset LastAttempt;
    }

    private readonly ConcurrentDictionary<string, Record> _records = new();

    /// <summary>
    /// If the key is currently locked out, returns the remaining wait; otherwise null.
    /// Call before validating a code.
    /// </summary>
    public TimeSpan? GetLockout(string key)
    {
        if (!_records.TryGetValue(key, out var rec)) return null;
        var now = DateTimeOffset.UtcNow;
        if (rec.LockedUntil > now) return rec.LockedUntil - now;
        return null;
    }

    /// <summary>Record a failed attempt and (re)compute the lockout for this key.</summary>
    public void RegisterFailure(string key)
    {
        var now = DateTimeOffset.UtcNow;
        var rec = _records.GetOrAdd(key, _ => new Record());
        lock (rec)
        {
            // Let old failures decay so a transient fat-finger doesn't lock a room for long.
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
    }

    /// <summary>Clear the record on a successful registration.</summary>
    public void RegisterSuccess(string key) => _records.TryRemove(key, out _);
}
