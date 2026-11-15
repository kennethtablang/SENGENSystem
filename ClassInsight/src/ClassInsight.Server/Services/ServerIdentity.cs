using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using ClassInsight.Shared;
using ClassInsight.Shared.Contracts;

namespace ClassInsight.Server.Services;

/// <summary>
/// The server's stable identity and deployment configuration, resolved once at startup
/// and shared by the hub, the discovery beacon and teacher auth. Carries the room name,
/// the LAN address clients should dial, the open/closed teacher-mode decision and the
/// effective teacher code.
///
/// Implements the plan's security model for teacher pairing (FR-SEC-02/03/06, NFR-SEC-04).
/// </summary>
public sealed class ServerIdentity
{
    public string ServerId { get; }
    public string RoomName { get; }
    public int Port { get; }

    /// <summary>LAN IPv4 address clients dial (also embedded in discovery responses).</summary>
    public string Host { get; }

    /// <summary>When true the server runs "open": teachers join with no code (FR-SEC-03).</summary>
    public bool AllowOpenTeacherMode { get; }

    /// <summary>
    /// The code a teacher must present when <see cref="AllowOpenTeacherMode"/> is false.
    /// Empty only when open. In closed mode with no configured code, one is generated.
    /// </summary>
    public string TeacherCode { get; }

    /// <summary>When false the UDP discovery beacon is not started (tests, or broadcast-hostile networks).</summary>
    public bool DiscoveryEnabled { get; }

    /// <summary>
    /// STUN/TURN servers handed to both WebRTC peers (relay fallback for segmented networks, P4).
    /// Empty on a flat lab LAN, where peers connect directly. Only given to registered clients,
    /// since TURN entries carry credentials.
    /// </summary>
    public IReadOnlyList<IceServerConfig> IceServers { get; }

    public ServerIdentity(IConfiguration configuration, ILogger<ServerIdentity> logger)
    {
        var section = configuration.GetSection("ClassInsight");

        ServerId = Guid.NewGuid().ToString("N");
        RoomName = section.GetValue<string>("RoomName") ?? "Classroom";
        Port = section.GetValue<int?>("Port") ?? Protocol.DefaultServerPort;
        AllowOpenTeacherMode = section.GetValue<bool?>("AllowOpenTeacherMode") ?? false;
        Host = ResolveLanAddress();
        DiscoveryEnabled = section.GetValue<bool?>("DiscoveryEnabled") ?? true;
        IceServers = section.GetSection("IceServers").Get<IceServerConfig[]>()?
            .Where(s => !string.IsNullOrWhiteSpace(s.Urls))
            .ToArray() ?? [];

        var configured = section.GetValue<string>("TeacherCode");
        if (AllowOpenTeacherMode)
        {
            TeacherCode = string.Empty;
            logger.LogWarning(
                "Teacher mode is OPEN: any teacher on the LAN can connect without a code. " +
                "This is intended for a trusted lab VLAN only. Set ClassInsight:AllowOpenTeacherMode=false to require a code.");
        }
        else if (!string.IsNullOrWhiteSpace(configured))
        {
            TeacherCode = configured.Trim();
            logger.LogInformation("Teacher mode is CLOSED. Teachers must present the configured join code.");
        }
        else
        {
            TeacherCode = GenerateCode();
            logger.LogInformation("Teacher mode is CLOSED. Generated one-time join code for this run.");
        }

        logger.LogInformation(
            "ClassInsight server \"{Room}\" ({ServerId}) listening on {Host}:{Port}{HubPath}",
            RoomName, ServerId, Host, Port, Protocol.HubPath);

        if (!AllowOpenTeacherMode)
        {
            // Printed prominently so the teacher at the podium can read it off the server console.
            Console.WriteLine();
            Console.WriteLine("  ┌───────────────────────────────────────────┐");
            Console.WriteLine($"  │  Teacher join code:  {TeacherCode,-21}│");
            Console.WriteLine("  └───────────────────────────────────────────┘");
            Console.WriteLine();
        }
    }

    /// <summary>Build the discovery reply advertised to probing clients.</summary>
    public DiscoveryResponse ToDiscoveryResponse() => new()
    {
        Kind = "response",
        ServerId = ServerId,
        RoomName = RoomName,
        Host = Host,
        Port = Port,
        HubPath = Protocol.HubPath,
        ProtocolVersion = Protocol.Version,
    };

    // Crockford base32: no I/L/O/U, so a code read off the console can't be mistyped as 1/0.
    private const string CodeAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>
    /// A one-time pairing code with 65 bits of entropy (13 base32 symbols), grouped for reading
    /// aloud, e.g. <c>7KQ2-M9XD-4TR8B</c>. FR-SEC-06 requires ≥ 64 bits so that, together with
    /// <see cref="TeacherAuthThrottle"/>, online guessing is infeasible.
    /// </summary>
    public static string GenerateCode()
    {
        Span<char> raw = stackalloc char[13];
        for (var i = 0; i < raw.Length; i++)
            raw[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        var s = new string(raw);
        return $"{s[..4]}-{s[4..8]}-{s[8..]}";
    }

    /// <summary>Canonical form for comparison: separators/whitespace dropped, upper-cased.</summary>
    public static string NormalizeCode(string? code) =>
        new string((code ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray())
            .ToUpperInvariant();

    /// <summary>
    /// Constant-time check of a presented teacher code (NFR-SEC-04). Both sides are normalised
    /// and hashed first, so neither the content nor the length of the real code leaks through
    /// timing. Always false when no code is configured.
    /// </summary>
    public bool VerifyTeacherCode(string? presented)
    {
        if (string.IsNullOrEmpty(TeacherCode)) return false;
        var a = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(NormalizeCode(presented)));
        var b = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(NormalizeCode(TeacherCode)));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// Pick the best LAN IPv4 to advertise: a private, operational, non-loopback address
    /// on a real adapter (Ethernet/Wi-Fi preferred). Falls back to any up IPv4.
    /// </summary>
    private static string ResolveLanAddress()
    {
        var candidates = new List<(IPAddress Addr, int Score)>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress.IsLoopback(ua.Address)) continue;

                var bytes = ua.Address.GetAddressBytes();
                // Skip APIPA 169.254.x.x unless nothing else exists.
                var isApipa = bytes[0] == 169 && bytes[1] == 254;

                var score = nic.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Ethernet => 3,
                    NetworkInterfaceType.Wireless80211 => 2,
                    _ => 1,
                };
                if (isApipa) score = 0;

                candidates.Add((ua.Address, score));
            }
        }

        var best = candidates.OrderByDescending(c => c.Score).FirstOrDefault();
        return best.Addr?.ToString() ?? "127.0.0.1";
    }
}
