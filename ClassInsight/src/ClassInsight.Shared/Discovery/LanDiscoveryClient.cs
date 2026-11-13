using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ClassInsight.Shared.Contracts;

namespace ClassInsight.Shared.Discovery;

/// <summary>
/// Zero-configuration LAN discovery used by both the student agent and the teacher app
/// to find the classroom server without any manual IP entry (AD-5, FR-DISC-03/04). It
/// broadcasts a <see cref="DiscoveryProbe"/> on UDP <see cref="Protocol.DiscoveryPort"/>
/// and returns the first valid <see cref="DiscoveryResponse"/> it hears — whether a
/// direct reply to the probe or one of the server's periodic beacons.
///
/// Every datagram is the magic token <see cref="Protocol.DiscoveryMagic"/> followed by
/// JSON, so stray UDP traffic is ignored. If a response carries no explicit host, the
/// datagram's own source address is used (the server answers from the interface that
/// routes back to this client), so the client only ever dials the responder itself
/// (NFR-SEC-05).
///
/// NOTE: this file was reconstructed from its call sites and the protocol specification
/// after the original was lost; behaviour matches the documented discovery contract.
/// </summary>
public sealed class LanDiscoveryClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _role;

    /// <param name="role">"student" or "teacher" — announced in the probe for logging/telemetry.</param>
    public LanDiscoveryClient(string role) => _role = role;

    /// <summary>
    /// Broadcast a probe and wait up to <paramref name="timeout"/> for a server to answer.
    /// Returns the first valid <see cref="DiscoveryResponse"/>, or null if none replied
    /// before the timeout (or cancellation).
    /// </summary>
    public async Task<DiscoveryResponse?> FindServerAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork)
        {
            EnableBroadcast = true,
        };
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        var probe = Encode(new DiscoveryProbe { Kind = "probe", Role = _role });
        var broadcast = new IPEndPoint(IPAddress.Broadcast, Protocol.DiscoveryPort);

        try
        {
            await udp.SendAsync(probe, probe.Length, broadcast);
        }
        catch (SocketException)
        {
            // Broadcast may be refused on some interfaces; still listen in case a beacon arrives.
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        while (!timeoutCts.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (SocketException)
            {
                continue;
            }

            var response = TryDecode(result.Buffer);
            if (response is null) continue;
            if (response.ProtocolVersion != Protocol.Version) continue;

            // If the server didn't embed a host, dial the address it answered from.
            if (string.IsNullOrWhiteSpace(response.Host))
                response = response with { Host = result.RemoteEndPoint.Address.ToString() };

            return response;
        }

        return null;
    }

    private static byte[] Encode(DiscoveryProbe probe)
    {
        var json = JsonSerializer.Serialize(probe, Json);
        return Encoding.UTF8.GetBytes(Protocol.DiscoveryMagic + json);
    }

    private static DiscoveryResponse? TryDecode(byte[] datagram)
    {
        var magic = Protocol.DiscoveryMagic;
        if (datagram.Length <= magic.Length) return null;

        var text = Encoding.UTF8.GetString(datagram);
        if (!text.StartsWith(magic, StringComparison.Ordinal)) return null;

        var json = text[magic.Length..];
        try
        {
            // A beacon and a probe reply share the response shape; a client's own probe
            // (Kind "probe") has no ServerId/host, so it deserialises to an empty response
            // we reject below.
            var response = JsonSerializer.Deserialize<DiscoveryResponse>(json, Json);
            if (response is null) return null;
            if (string.IsNullOrEmpty(response.ServerId) && string.IsNullOrWhiteSpace(response.Host))
                return null; // not a server announcement (e.g. our own probe echoed back)
            return response;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
