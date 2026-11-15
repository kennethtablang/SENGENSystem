namespace ClassInsight.Shared.Contracts;

/// <summary>
/// WebRTC SDP exchange. The teacher (viewer) asks to watch a student; the student
/// (media sender) produces the offer, the teacher answers. The server only relays
/// these blobs between the two connection ids — it never parses the SDP.
/// </summary>
public sealed record SdpMessage
{
    /// <summary>Connection id of the peer this message is addressed to.</summary>
    public string TargetConnectionId { get; init; } = string.Empty;

    /// <summary>Connection id of the sender (filled in by the server on relay).</summary>
    public string FromConnectionId { get; init; } = string.Empty;

    /// <summary>"offer" or "answer".</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Raw SDP payload.</summary>
    public string Sdp { get; init; } = string.Empty;
}

/// <summary>A single ICE candidate relayed between peers during negotiation.</summary>
public sealed record IceCandidateMessage
{
    public string TargetConnectionId { get; init; } = string.Empty;
    public string FromConnectionId { get; init; } = string.Empty;

    public string Candidate { get; init; } = string.Empty;
    public string? SdpMid { get; init; }
    public int? SdpMLineIndex { get; init; }
}

/// <summary>Teacher → server request to begin viewing a student's screen.</summary>
public sealed record StreamRequest
{
    public string StudentConnectionId { get; init; } = string.Empty;

    /// <summary>
    /// Requested quality hint so the agent can scale encoding to how the tile is shown
    /// (thumbnail in the grid vs. a full-screen focused view).
    /// </summary>
    public StreamQuality Quality { get; init; } = StreamQuality.Thumbnail;

    /// <summary>
    /// Which of the student's displays to stream (index into <see cref="StudentInfo.Displays"/>;
    /// 0 = primary). Out-of-range values fall back to the primary display (FR-AGENT-04).
    /// </summary>
    public int DisplayIndex { get; init; }
}

/// <summary>
/// A STUN or TURN server handed to both WebRTC peers so a stream can still be set up across a
/// segmented network where direct LAN connectivity fails (relay fallback, P4). Configured on
/// the server under <c>ClassInsight:IceServers</c>; empty on a flat lab LAN.
/// </summary>
public sealed record IceServerConfig
{
    /// <summary>e.g. <c>stun:10.0.0.5:3478</c> or <c>turn:10.0.0.5:3478</c>.</summary>
    public string Urls { get; init; } = string.Empty;
    public string? Username { get; init; }
    public string? Credential { get; init; }
}

public enum StreamQuality
{
    /// <summary>Low fps / downscaled — suitable for a grid of many tiles.</summary>
    Thumbnail = 0,
    /// <summary>Full resolution / higher fps — for a single focused student.</summary>
    Focus = 1,
}
