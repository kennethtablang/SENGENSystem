namespace ClassInsight.Shared;

/// <summary>
/// Wire-protocol constants shared by the student agent, the teacher app and the
/// coordination server. Everything on the LAN agrees on these values so that
/// agents can be auto-discovered and "absorbed" without any manual configuration.
/// </summary>
public static class Protocol
{
    /// <summary>Bump when a breaking change is made to any contract below.</summary>
    public const int Version = 1;

    /// <summary>Magic token prefixing every discovery datagram so we ignore stray UDP traffic.</summary>
    public const string DiscoveryMagic = "CLASSINSIGHT/1";

    /// <summary>
    /// UDP port the server listens on for discovery probes and where it emits
    /// periodic beacons. Must be identical on every machine on the LAN.
    /// </summary>
    public const int DiscoveryPort = 47800;

    /// <summary>Default Kestrel HTTP port for the SignalR coordination endpoint.</summary>
    public const int DefaultServerPort = 5080;

    /// <summary>SignalR hub route. Clients connect to http://{ip}:{port}{HubPath}.</summary>
    public const string HubPath = "/hub/classroom";

    /// <summary>How often (ms) the server emits an unsolicited discovery beacon.</summary>
    public const int BeaconIntervalMs = 2000;

    /// <summary>
    /// HTTP status endpoint (FR-SRV-03). Also how a client resolves a manually entered server
    /// address on networks where UDP broadcast is blocked (NFR-COMPAT-02).
    /// </summary>
    public const string StatusPath = "/status";

    /// <summary>Names of hub methods the SERVER invokes ON clients.</summary>
    public static class ClientMethods
    {
        public const string RosterUpdated = nameof(RosterUpdated);
        public const string StudentJoined = nameof(StudentJoined);
        public const string StudentLeft = nameof(StudentLeft);
        public const string StudentStateChanged = nameof(StudentStateChanged);

        // WebRTC signalling relayed between a teacher (viewer) and a student (sender).
        public const string StreamRequested = nameof(StreamRequested);
        public const string StreamStopRequested = nameof(StreamStopRequested);
        public const string ReceiveOffer = nameof(ReceiveOffer);
        public const string ReceiveAnswer = nameof(ReceiveAnswer);
        public const string ReceiveIceCandidate = nameof(ReceiveIceCandidate);

        // Live telemetry fan-out.
        public const string TelemetryUpdated = nameof(TelemetryUpdated);
    }

    /// <summary>Names of hub methods CLIENTS invoke ON the server.</summary>
    public static class ServerMethods
    {
        public const string RegisterStudent = nameof(RegisterStudent);
        public const string RegisterTeacher = nameof(RegisterTeacher);
        public const string GetRoster = nameof(GetRoster);

        /// <summary>STUN/TURN servers the room is configured with (relay fallback, P4).</summary>
        public const string GetIceServers = nameof(GetIceServers);

        public const string RequestStream = nameof(RequestStream);
        public const string StopStream = nameof(StopStream);
        public const string SendOffer = nameof(SendOffer);
        public const string SendAnswer = nameof(SendAnswer);
        public const string SendIceCandidate = nameof(SendIceCandidate);

        public const string PushTelemetry = nameof(PushTelemetry);
    }
}
