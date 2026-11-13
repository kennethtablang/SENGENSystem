using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ClassInsight.Shared;
using ClassInsight.Shared.Contracts;

namespace ClassInsight.Server.Services;

/// <summary>
/// Makes the server findable with zero client configuration (FR-DISC-01/02, AD-5). It
/// listens on UDP <see cref="Protocol.DiscoveryPort"/> for <see cref="DiscoveryProbe"/>
/// datagrams and unicasts a <see cref="DiscoveryResponse"/> straight back to the prober,
/// and additionally emits a periodic broadcast beacon. Every datagram is the magic token
/// followed by JSON so unrelated UDP traffic is ignored.
///
/// Reconstructed from the shared discovery contract and <c>LanDiscoveryClient</c> after
/// the original was lost; the wire shape matches what clients already expect.
/// </summary>
public sealed class DiscoveryBeaconService : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ServerIdentity _identity;
    private readonly ILogger<DiscoveryBeaconService> _logger;

    public DiscoveryBeaconService(ServerIdentity identity, ILogger<DiscoveryBeaconService> logger)
    {
        _identity = identity;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        UdpClient udp;
        try
        {
            udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, Protocol.DiscoveryPort));
        }
        catch (SocketException ex)
        {
            _logger.LogError(ex,
                "Could not bind UDP discovery port {Port}. Auto-discovery is disabled; clients must dial {Host}:{ServerPort} directly.",
                Protocol.DiscoveryPort, _identity.Host, _identity.Port);
            return;
        }

        _logger.LogInformation("Discovery listening on UDP {Port}.", Protocol.DiscoveryPort);

        var beacon = Task.Run(() => BeaconLoopAsync(udp, stoppingToken), stoppingToken);
        var listen = ListenLoopAsync(udp, stoppingToken);

        try { await Task.WhenAll(beacon, listen); }
        finally { udp.Dispose(); }
    }

    private async Task ListenLoopAsync(UdpClient udp, CancellationToken ct)
    {
        var payload = EncodeResponse();

        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { continue; }

            if (!IsProbe(result.Buffer)) continue;

            try
            {
                // Unicast the response to the interface/port the probe came from, so the
                // client dials an address that routes back to it.
                await udp.SendAsync(payload, payload.Length, result.RemoteEndPoint);
            }
            catch (SocketException) { /* client may have moved on */ }
        }
    }

    private async Task BeaconLoopAsync(UdpClient udp, CancellationToken ct)
    {
        var payload = EncodeResponse();
        var broadcast = new IPEndPoint(IPAddress.Broadcast, Protocol.DiscoveryPort);

        while (!ct.IsCancellationRequested)
        {
            try { await udp.SendAsync(payload, payload.Length, broadcast); }
            catch (SocketException) { /* some adapters refuse broadcast */ }

            try { await Task.Delay(Protocol.BeaconIntervalMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private byte[] EncodeResponse()
    {
        var json = JsonSerializer.Serialize(_identity.ToDiscoveryResponse(), Json);
        return Encoding.UTF8.GetBytes(Protocol.DiscoveryMagic + json);
    }

    private static bool IsProbe(byte[] datagram)
    {
        var magic = Protocol.DiscoveryMagic;
        if (datagram.Length <= magic.Length) return false;

        var text = Encoding.UTF8.GetString(datagram);
        if (!text.StartsWith(magic, StringComparison.Ordinal)) return false;

        try
        {
            var probe = JsonSerializer.Deserialize<DiscoveryProbe>(text[magic.Length..], Json);
            return probe is { Kind: "probe" };
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
