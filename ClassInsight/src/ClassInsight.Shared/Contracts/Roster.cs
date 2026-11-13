namespace ClassInsight.Shared.Contracts;

public enum ClientRole
{
    Unknown = 0,
    Student = 1,
    Teacher = 2,
}

/// <summary>Live connection state of a student as seen by the server.</summary>
public enum StudentState
{
    Online = 0,
    Streaming = 1,
    Idle = 2,
    Offline = 3,
}

/// <summary>Identifying/hardware info an agent announces when it registers.</summary>
public sealed record StudentInfo
{
    /// <summary>
    /// Stable per-machine id (e.g. hashed hardware id). Lets a student keep its tile
    /// across reconnects instead of appearing as a brand-new machine each time.
    /// </summary>
    public string MachineId { get; init; } = string.Empty;

    public string MachineName { get; init; } = string.Empty;

    /// <summary>Currently signed-in Windows user, if resolvable.</summary>
    public string? UserName { get; init; }

    public string OsVersion { get; init; } = string.Empty;

    public string IpAddress { get; init; } = string.Empty;

    /// <summary>Native screen resolution, used by the teacher grid for aspect ratio.</summary>
    public int ScreenWidth { get; init; }
    public int ScreenHeight { get; init; }

    public string AgentVersion { get; init; } = string.Empty;

    /// <summary>
    /// Wire-protocol version the agent was built against. Defaults to the <see cref="Protocol.Version"/>
    /// of the shared library it is compiled with, so the server can reject an incompatible
    /// agent at registration (FR-DISC-06) without any client-side plumbing.
    /// </summary>
    public int ProtocolVersion { get; init; } = Protocol.Version;
}

public sealed record TeacherInfo
{
    public string DeviceName { get; init; } = string.Empty;
    public string Platform { get; init; } = string.Empty; // Windows | macOS | iOS | Android
    public string AppVersion { get; init; } = string.Empty;

    /// <summary>
    /// Pairing code the admin configured on the server (<c>ClassInsight:TeacherCode</c>).
    /// Only required to register as a teacher; empty when the server runs open.
    /// Students never supply this — their auto-join flow is unchanged.
    /// </summary>
    public string? JoinCode { get; init; }

    /// <summary>
    /// Wire-protocol version the teacher app was built against (see <see cref="StudentInfo.ProtocolVersion"/>).
    /// Lets the server reject an incompatible teacher at registration (FR-DISC-06).
    /// </summary>
    public int ProtocolVersion { get; init; } = Protocol.Version;
}

/// <summary>A student entry as projected to teachers in the roster/dashboard.</summary>
public sealed record StudentSnapshot
{
    public string ConnectionId { get; init; } = string.Empty;
    public string MachineId { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;
    public string? UserName { get; init; }
    public string IpAddress { get; init; } = string.Empty;
    public int ScreenWidth { get; init; }
    public int ScreenHeight { get; init; }
    public StudentState State { get; init; } = StudentState.Online;
    public DateTimeOffset ConnectedAt { get; init; }

    /// <summary>Most recent telemetry sample, if any has arrived yet.</summary>
    public TelemetrySnapshot? LastTelemetry { get; init; }
}

/// <summary>Full roster payload pushed to teachers.</summary>
public sealed record RosterSnapshot
{
    public IReadOnlyList<StudentSnapshot> Students { get; init; } = Array.Empty<StudentSnapshot>();
    public int TeacherCount { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}
