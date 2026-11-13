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
/// Reconstructed from its call sites (ClassroomHub / DiscoveryBeaconService / Program)
/// and the plan's security model (FR-SEC-02/03) after the original was lost.
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

    public ServerIdentity(IConfiguration configuration, ILogger<ServerIdentity> logger)
    {
        var section = configuration.GetSection("ClassInsight");

        ServerId = Guid.NewGuid().ToString("N");
        RoomName = section.GetValue<string>("RoomName") ?? "Classroom";
        Port = section.GetValue<int?>("Port") ?? Protocol.DefaultServerPort;
        AllowOpenTeacherMode = section.GetValue<bool?>("AllowOpenTeacherMode") ?? false;
        Host = ResolveLanAddress();

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

    private static string GenerateCode()
    {
        // 6 digits, easy to read aloud; not a secret store — just a pairing gate.
        return RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
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
