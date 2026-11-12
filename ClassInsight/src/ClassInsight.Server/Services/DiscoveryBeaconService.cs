using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ClassInsight.Shared;
using ClassInsight.Shared.Contracts;

namespace ClassInsight.Server.Services;

/// <summary>
/// LAN auto-discovery. This is what lets a student agent (or teacher app) be
/// "absorbed" by the server with zero configuration: the client broadcasts a UDP
/// probe, and this service answers with the exact hub URL to dial. It also emits
/// periodic beacons so passively-listening clients can latch on without probing.
///
/// Nothing here requires the student to do anything — the agent, deployed on the
/// lab machine, discovers and connects the instant it launches.
/// </summary>
public sealed class DiscoveryBeaconService : BackgroundService
{
    private readonly ServerIdentity _identity;
    private readonly ILogger<DiscoveryBeaconService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly byte[] MagicPrefix = Encoding.UTF8.GetBytes(Protocol.DiscoveryMagic + "\n");

    public DiscoveryBeaconService(ServerIdentity identity, ILogger<DiscoveryBeaconService> logger)
    {
        _identity = identity;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.EnableBroadcast = true;
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, Protocol.DiscoveryPort));

        _logger.LogInformation(
            "Discovery listening on udp/{Port} (server {Id}, room \"{Room}\")",
            Protocol.DiscoveryPort, _identity.ServerId, _identity.RoomName);

        var beaconLoop = BeaconLoopAsync(udp, stoppingToken);
        var receiveLoop = ReceiveLoopAsync(udp, stoppingToken);
        await Task.WhenAll(beaconLoop, receiveLoop);
    }

    private async Task ReceiveLoopAsync(UdpClient udp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException ex)
            {
                _logger.LogDebug(ex, "Discovery receive error; continuing");
                continue;
            }

            if (!TryReadPayload(result.Buffer, out var json)) continue;

            DiscoveryProbe? probe;
            try { probe = JsonSerializer.Deserialize<DiscoveryProbe>(json, JsonOpts); }
            catch { continue; }
            if (probe is null || probe.Kind != "probe") continue;

            var host = LocalAddressFor(result.RemoteEndPoint.Address);
            var response = BuildResponse(host);
            var bytes = Encode(response);

            try
            {
                await udp.SendAsync(bytes, bytes.Length, result.RemoteEndPoint);
                _logger.LogDebug("Answered {Role} probe from {Remote} with {Host}",
                    probe.Role ?? "?", result.RemoteEndPoint, host);
            }
            catch (SocketException ex)
            {
                _logger.LogDebug(ex, "Failed to answer probe from {Remote}", result.RemoteEndPoint);
            }
        }
    }

    private async Task BeaconLoopAsync(UdpClient udp, CancellationToken ct)
    {
        var broadcast = new IPEndPoint(IPAddress.Broadcast, Protocol.DiscoveryPort);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var host = PrimaryLocalAddress();
                var bytes = Encode(BuildResponse(host) with { Kind = "beacon" });
                await udp.SendAsync(bytes, bytes.Length, broadcast);
            }
            catch (SocketException ex)
            {
                _logger.LogDebug(ex, "Beacon send failed");
            }

            try { await Task.Delay(Protocol.BeaconIntervalMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private DiscoveryResponse BuildResponse(string host) => new()
    {
        ServerId = _identity.ServerId,
        RoomName = _identity.RoomName,
        Host = host,
        Port = _identity.Port,
    };

    private static byte[] Encode(DiscoveryResponse response)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(response, JsonOpts);
        var buffer = new byte[MagicPrefix.Length + json.Length];
        Buffer.BlockCopy(MagicPrefix, 0, buffer, 0, MagicPrefix.Length);
        Buffer.BlockCopy(json, 0, buffer, MagicPrefix.Length, json.Length);
        return buffer;
    }

    private static bool TryReadPayload(byte[] datagram, out string json)
    {
        json = string.Empty;
        if (datagram.Length <= MagicPrefix.Length) return false;
        for (var i = 0; i < MagicPrefix.Length; i++)
            if (datagram[i] != MagicPrefix[i]) return false;
        json = Encoding.UTF8.GetString(datagram, MagicPrefix.Length, datagram.Length - MagicPrefix.Length);
        return true;
    }

    /// <summary>
    /// The server IP that the given remote can actually route to. We ask the OS by
    /// "connecting" a throwaway UDP socket to the remote and reading the chosen
    /// local endpoint — no packets are sent.
    /// </summary>
    private static string LocalAddressFor(IPAddress remote)
    {
        try
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect(remote, 9); // discard port; no traffic leaves the box
            if (probe.LocalEndPoint is IPEndPoint ep && !Equals(ep.Address, IPAddress.Any))
                return ep.Address.ToString();
        }
        catch (SocketException) { /* fall through */ }
        return PrimaryLocalAddress();
    }

    /// <summary>Best-guess primary IPv4 for beacons (where no specific remote exists).</summary>
    private static string PrimaryLocalAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var candidate = host.AddressList
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork
                                     && !IPAddress.IsLoopback(a));
            if (candidate is not null) return candidate.ToString();
        }
        catch { /* ignore */ }
        return IPAddress.Loopback.ToString();
    }
}
