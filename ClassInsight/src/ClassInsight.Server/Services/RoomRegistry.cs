using System.Collections.Concurrent;
using ClassInsight.Shared.Contracts;

namespace ClassInsight.Server.Services;

/// <summary>
/// The authoritative in-memory roster for the room: which students and teachers are
/// connected, their last telemetry, and the SignalR connection ids used to relay
/// signalling. Thread-safe; the hub mutates it from many connection callbacks at once.
///
/// There is exactly one room per server process (one lab). Reconstructed from the hub's
/// usage and the roster contracts after the original was lost.
/// </summary>
public sealed class RoomRegistry
{
    private readonly ConcurrentDictionary<string, StudentEntry> _students = new();   // key: connectionId
    private readonly ConcurrentDictionary<string, TeacherEntry> _teachers = new();   // key: connectionId

    public sealed class StudentEntry
    {
        public required string ConnectionId { get; init; }
        public required StudentInfo Info { get; init; }
        public StudentState State { get; set; } = StudentState.Online;
        public TelemetrySnapshot? LastTelemetry { get; set; }
        public DateTimeOffset ConnectedAt { get; } = DateTimeOffset.UtcNow;

        public StudentSnapshot ToSnapshot() => new()
        {
            ConnectionId = ConnectionId,
            MachineId = Info.MachineId,
            MachineName = Info.MachineName,
            UserName = Info.UserName,
            IpAddress = Info.IpAddress,
            ScreenWidth = Info.ScreenWidth,
            ScreenHeight = Info.ScreenHeight,
            State = State,
            ConnectedAt = ConnectedAt,
            LastTelemetry = LastTelemetry,
        };
    }

    public sealed class TeacherEntry
    {
        public required string ConnectionId { get; init; }
        public required TeacherInfo Info { get; init; }
        public DateTimeOffset ConnectedAt { get; } = DateTimeOffset.UtcNow;
    }

    // ---- students ---------------------------------------------------------

    public StudentEntry AddOrUpdateStudent(string connectionId, StudentInfo info)
    {
        var entry = new StudentEntry { ConnectionId = connectionId, Info = info };
        _students[connectionId] = entry;
        return entry;
    }

    public bool TryGetStudent(string connectionId, out StudentEntry entry)
        => _students.TryGetValue(connectionId, out entry!);

    public bool RemoveStudent(string connectionId, out StudentEntry? entry)
    {
        var removed = _students.TryRemove(connectionId, out var e);
        entry = e;
        return removed;
    }

    public bool SetStudentState(string connectionId, StudentState state)
    {
        if (!_students.TryGetValue(connectionId, out var entry)) return false;
        entry.State = state;
        return true;
    }

    public StudentEntry? ApplyTelemetry(string connectionId, TelemetrySnapshot telemetry)
    {
        if (!_students.TryGetValue(connectionId, out var entry)) return null;
        entry.LastTelemetry = telemetry;
        return entry;
    }

    public bool IsStudent(string connectionId) => _students.ContainsKey(connectionId);

    // ---- teachers ---------------------------------------------------------

    public void AddOrUpdateTeacher(string connectionId, TeacherInfo info)
        => _teachers[connectionId] = new TeacherEntry { ConnectionId = connectionId, Info = info };

    public bool RemoveTeacher(string connectionId) => _teachers.TryRemove(connectionId, out _);

    public bool IsTeacher(string connectionId) => _teachers.ContainsKey(connectionId);

    public IReadOnlyCollection<string> TeacherConnectionIds => _teachers.Keys.ToArray();

    public int TeacherCount => _teachers.Count;

    // ---- projection -------------------------------------------------------

    public RosterSnapshot BuildRoster() => new()
    {
        Students = _students.Values
            .OrderBy(s => s.Info.MachineName, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.ToSnapshot())
            .ToArray(),
        TeacherCount = _teachers.Count,
        GeneratedAt = DateTimeOffset.UtcNow,
    };
}
