using System.Collections.Concurrent;
using ClassInsight.Shared.Contracts;

namespace ClassInsight.Server.Services;

/// <summary>
/// Thread-safe, in-memory source of truth for who is connected to this classroom
/// server. There is no database and no login: a machine exists in the roster for
/// exactly as long as its SignalR connection is alive. Deployed on school-owned
/// lab machines, agents register automatically the moment the app opens.
/// </summary>
public sealed class RoomRegistry
{
    private readonly ConcurrentDictionary<string, StudentEntry> _students = new();
    private readonly ConcurrentDictionary<string, TeacherEntry> _teachers = new();

    /// <summary>
    /// Authorised viewing sessions, keyed "teacherConn|studentConn". A WebRTC
    /// signalling message is only relayed between a pair that appears here, which
    /// the server adds exclusively via <see cref="AuthorizeView"/> when a teacher
    /// calls RequestStream. This stops any client from injecting SDP/ICE into
    /// arbitrary connections.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _authorizedPairs = new();

    private static string PairKey(string teacherConn, string studentConn)
        => $"{teacherConn}|{studentConn}";

    public sealed class StudentEntry
    {
        public required string ConnectionId { get; init; }
        public required StudentInfo Info { get; set; }
        public StudentState State { get; set; } = StudentState.Online;
        public DateTimeOffset ConnectedAt { get; init; } = DateTimeOffset.UtcNow;
        public TelemetrySnapshot? LastTelemetry { get; set; }
    }

    public sealed class TeacherEntry
    {
        public required string ConnectionId { get; init; }
        public required TeacherInfo Info { get; set; }
        public DateTimeOffset ConnectedAt { get; init; } = DateTimeOffset.UtcNow;
    }

    public int TeacherCount => _teachers.Count;
    public int StudentCount => _students.Count;

    public void AddOrUpdateStudent(string connectionId, StudentInfo info)
    {
        _students.AddOrUpdate(
            connectionId,
            _ => new StudentEntry { ConnectionId = connectionId, Info = info },
            (_, existing) => { existing.Info = info; return existing; });
    }

    public void AddOrUpdateTeacher(string connectionId, TeacherInfo info)
    {
        _teachers.AddOrUpdate(
            connectionId,
            _ => new TeacherEntry { ConnectionId = connectionId, Info = info },
            (_, existing) => { existing.Info = info; return existing; });
    }

    /// <summary>Removes a client on disconnect. Returns its role so the hub can notify peers.</summary>
    public ClientRole Remove(string connectionId)
    {
        RemoveAllPairsFor(connectionId);
        if (_students.TryRemove(connectionId, out _)) return ClientRole.Student;
        if (_teachers.TryRemove(connectionId, out _)) return ClientRole.Teacher;
        return ClientRole.Unknown;
    }

    // ---- View authorisation (WebRTC signalling gate) --------------------

    public void AuthorizeView(string teacherConn, string studentConn)
        => _authorizedPairs[PairKey(teacherConn, studentConn)] = 1;

    public void RevokeView(string teacherConn, string studentConn)
        => _authorizedPairs.TryRemove(PairKey(teacherConn, studentConn), out _);

    public bool IsViewAuthorized(string teacherConn, string studentConn)
        => _authorizedPairs.ContainsKey(PairKey(teacherConn, studentConn));

    /// <summary>True if this student currently has any authorised teacher viewer.</summary>
    public bool StudentHasViewer(string studentConn)
        => _authorizedPairs.Keys.Any(k => k.EndsWith('|' + studentConn));

    private void RemoveAllPairsFor(string connectionId)
    {
        foreach (var key in _authorizedPairs.Keys)
        {
            var sep = key.IndexOf('|');
            if (sep < 0) continue;
            var teacher = key.AsSpan(0, sep);
            var student = key.AsSpan(sep + 1);
            if (teacher.SequenceEqual(connectionId) || student.SequenceEqual(connectionId))
                _authorizedPairs.TryRemove(key, out _);
        }
    }

    public bool TryGetStudent(string connectionId, out StudentEntry entry)
        => _students.TryGetValue(connectionId, out entry!);

    public bool IsTeacher(string connectionId) => _teachers.ContainsKey(connectionId);

    public void SetStudentState(string connectionId, StudentState state)
    {
        if (_students.TryGetValue(connectionId, out var entry))
            entry.State = state;
    }

    public void UpdateTelemetry(string connectionId, TelemetrySnapshot snapshot)
    {
        if (_students.TryGetValue(connectionId, out var entry))
            entry.LastTelemetry = snapshot;
    }

    public IReadOnlyList<string> TeacherConnectionIds() => _teachers.Keys.ToArray();

    public StudentSnapshot ProjectStudent(StudentEntry e) => new()
    {
        ConnectionId = e.ConnectionId,
        MachineId = e.Info.MachineId,
        MachineName = e.Info.MachineName,
        UserName = e.Info.UserName,
        IpAddress = e.Info.IpAddress,
        ScreenWidth = e.Info.ScreenWidth,
        ScreenHeight = e.Info.ScreenHeight,
        State = e.State,
        ConnectedAt = e.ConnectedAt,
        LastTelemetry = e.LastTelemetry,
    };

    public RosterSnapshot BuildRoster() => new()
    {
        Students = _students.Values
            .OrderBy(s => s.Info.MachineName, StringComparer.OrdinalIgnoreCase)
            .Select(ProjectStudent)
            .ToArray(),
        TeacherCount = _teachers.Count,
    };
}
