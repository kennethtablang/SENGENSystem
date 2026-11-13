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
}

public enum StreamQuality
{
    /// <summary>Low fps / downscaled — suitable for a grid of many tiles.</summary>
    Thumbnail = 0,
    /// <summary>Full resolution / higher fps — for a single focused student.</summary>
    Focus = 1,
}
