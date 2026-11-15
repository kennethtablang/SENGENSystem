namespace ClassInsight.Shared.Contracts;

/// <summary>
/// Sent by an agent/teacher as a UDP broadcast to find the classroom server, and
/// emitted unsolicited by the server as a beacon. One datagram, JSON-encoded,
/// prefixed by <see cref="Protocol.DiscoveryMagic"/>.
/// </summary>
public sealed record DiscoveryProbe
{
    /// <summary>"probe" when a client is searching, "beacon" when the server announces.</summary>
    public string Kind { get; init; } = "probe";

    /// <summary>Role of the sender if it is a client probe (student|teacher), else null.</summary>
    public string? Role { get; init; }

    public int ProtocolVersion { get; init; } = Protocol.Version;
}

/// <summary>
/// The server's reply to a <see cref="DiscoveryProbe"/>. Carries everything a
/// client needs to open the SignalR connection with zero manual setup.
/// </summary>
public sealed record DiscoveryResponse
{
    public string Kind { get; init; } = "response";

    /// <summary>Stable id for this server instance (survives client reconnects).</summary>
    public string ServerId { get; init; } = string.Empty;

    /// <summary>Human-facing room/lab name shown in the teacher UI.</summary>
    public string RoomName { get; init; } = "Classroom";

    /// <summary>IP address clients should dial. Resolved from the receiving interface.</summary>
    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = Protocol.DefaultServerPort;

    public string HubPath { get; init; } = Protocol.HubPath;

    public int ProtocolVersion { get; init; } = Protocol.Version;

    /// <summary>Convenience: the full hub URL a client can hand straight to HubConnectionBuilder.</summary>
    public string HubUrl => $"http://{Host}:{Port}{HubPath}";
}

/// <summary>
/// Payload of the server's HTTP status endpoint (<see cref="Protocol.StatusPath"/>, FR-SRV-03).
/// A client that was given a server address by hand (broadcast blocked, NFR-COMPAT-02) reads
/// this to learn the room before opening the hub.
/// </summary>
public sealed record ServerStatus
{
    public string ServerId { get; init; } = string.Empty;
    public string RoomName { get; init; } = string.Empty;
    public int ProtocolVersion { get; init; }
    public int Students { get; init; }
    public int Teachers { get; init; }
}
