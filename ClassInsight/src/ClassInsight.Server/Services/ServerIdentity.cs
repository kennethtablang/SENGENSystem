namespace ClassInsight.Server.Services;

/// <summary>
/// Stable identity/config for this running server instance. Generated once at
/// startup and shared with the discovery service and hub.
/// </summary>
public sealed class ServerIdentity
{
    public string ServerId { get; } = Guid.NewGuid().ToString("N")[..12];
    public required string RoomName { get; init; }
    public required int Port { get; init; }

    /// <summary>
    /// The effective teacher pairing code. Non-null whenever the server enforces
    /// teacher auth — whether the code was configured or auto-generated at startup.
    /// Null only in explicit open mode.
    /// </summary>
    public string? TeacherCode { get; init; }

    /// <summary>
    /// True only when the admin explicitly set <c>ClassInsight:AllowOpenTeacherMode=true</c>.
    /// The server then performs no teacher authentication — any device on the network
    /// can become a teacher. Off by default so the system fails closed.
    /// </summary>
    public bool OpenTeacherMode { get; init; }

    /// <summary>Teacher registration is gated unless the admin opted into open mode.</summary>
    public bool RequiresTeacherCode => !OpenTeacherMode;
}
