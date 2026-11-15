using System.Net;
using System.Text;
using System.Text.Json;
using ClassInsight.Shared.Contracts;

namespace ClassInsight.Shared.Discovery;

/// <summary>
/// The one encoder/decoder for discovery datagrams, shared by the server's beacon and every
/// client so the wire shape can't drift (NFR-MAIN-01). A datagram is the magic token
/// <see cref="Protocol.DiscoveryMagic"/> followed by web-default JSON.
/// </summary>
public static class DiscoveryCodec
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Datagrams bigger than this are never ours; reject before decoding.</summary>
    public const int MaxDatagramBytes = 4096;

    public static byte[] Encode(DiscoveryProbe probe) => Encode((object)probe);

    public static byte[] Encode(DiscoveryResponse response) => Encode((object)response);

    private static byte[] Encode(object message) =>
        Encoding.UTF8.GetBytes(Protocol.DiscoveryMagic + JsonSerializer.Serialize(message, message.GetType(), Json));

    /// <summary>True when the datagram is a client probe (what the server answers).</summary>
    public static bool IsProbe(ReadOnlySpan<byte> datagram)
    {
        var json = StripMagic(datagram);
        if (json is null) return false;
        try
        {
            var probe = JsonSerializer.Deserialize<DiscoveryProbe>(json, Json);
            return probe is { Kind: "probe" };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Decode a server announcement (probe reply or beacon) received from <paramref name="source"/>.
    ///
    /// Security (NFR-SEC-05): whatever host/path the datagram claims is ignored. The result
    /// always points at the address the datagram actually came from, on the fixed
    /// <see cref="Protocol.HubPath"/>, so a forged response can't steer a client to some
    /// other machine or URL. Only the port is taken from the payload (validated).
    /// </summary>
    public static DiscoveryResponse? TryDecodeResponse(ReadOnlySpan<byte> datagram, IPAddress source)
    {
        var json = StripMagic(datagram);
        if (json is null) return null;

        DiscoveryResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<DiscoveryResponse>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }

        // A client's own probe echoed back deserialises to an empty response: not a server.
        if (response is null || string.IsNullOrEmpty(response.ServerId)) return null;
        if (response.Kind is not ("response" or "beacon")) return null;
        if (response.ProtocolVersion != Protocol.Version) return null;
        if (response.Port is <= 0 or > 65535) return null;

        var host = source.IsIPv4MappedToIPv6 ? source.MapToIPv4() : source;
        return response with
        {
            Host = host.ToString(),
            HubPath = Protocol.HubPath,
            RoomName = Sanitize(response.RoomName),
        };
    }

    private static string? StripMagic(ReadOnlySpan<byte> datagram)
    {
        if (datagram.Length > MaxDatagramBytes) return null;
        var magic = Protocol.DiscoveryMagic;
        if (datagram.Length <= magic.Length) return null;

        var text = Encoding.UTF8.GetString(datagram);
        return text.StartsWith(magic, StringComparison.Ordinal) ? text[magic.Length..] : null;
    }

    // Room names are shown in the teacher UI; keep them printable and short.
    private static string Sanitize(string? room)
    {
        if (string.IsNullOrWhiteSpace(room)) return "Classroom";
        var clean = new string(room.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length > 64 ? clean[..64] : clean;
    }
}
