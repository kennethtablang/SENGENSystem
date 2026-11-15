using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClassInsight.Shared.Contracts;

namespace ClassInsight.Shared.Discovery;

/// <summary>
/// Fallback for networks that block UDP broadcast (NFR-COMPAT-02, R-2): resolve a server from
/// an address typed by the teacher or set by IT in the agent's config (<c>host</c> or
/// <c>host:port</c>). It reads the server's <see cref="Protocol.StatusPath"/> endpoint to learn
/// the room and checks the protocol version, then returns the same
/// <see cref="DiscoveryResponse"/> shape broadcast discovery would have produced, so the rest of
/// the connect path is identical.
/// </summary>
public static class ManualServer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Parse <c>host</c> / <c>host:port</c> (IPv4 or DNS name). Returns false on anything else.</summary>
    public static bool TryParse(string? address, out string host, out int port)
    {
        host = string.Empty;
        port = Protocol.DefaultServerPort;
        if (string.IsNullOrWhiteSpace(address)) return false;

        var text = address.Trim();
        // Tolerate a pasted URL ("http://10.0.0.5:5080/...").
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            host = uri.Host;
            port = uri.IsDefaultPort ? Protocol.DefaultServerPort : uri.Port;
            return Uri.CheckHostName(host) is UriHostNameType.IPv4 or UriHostNameType.Dns;
        }

        var colon = text.LastIndexOf(':');
        if (colon > 0)
        {
            if (!int.TryParse(text[(colon + 1)..], out port) || port is <= 0 or > 65535) return false;
            text = text[..colon];
        }

        host = text;
        return Uri.CheckHostName(host) is UriHostNameType.IPv4 or UriHostNameType.Dns;
    }

    /// <summary>
    /// Query the server's status endpoint. Returns null if the address is invalid, unreachable,
    /// not a ClassInsight server, or speaks a different protocol version.
    /// </summary>
    public static async Task<DiscoveryResponse?> ResolveAsync(string address, TimeSpan timeout, CancellationToken ct)
    {
        if (!TryParse(address, out var host, out var port)) return null;

        using var http = new HttpClient { Timeout = timeout };
        ServerStatus? status;
        try
        {
            status = await http.GetFromJsonAsync<ServerStatus>(
                new UriBuilder("http", host, port, Protocol.StatusPath).Uri, Json, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            return null;
        }

        if (status is null || string.IsNullOrEmpty(status.ServerId)) return null;
        if (status.ProtocolVersion != Protocol.Version) return null;

        // Prefer a literal IP for the hub URL when the user typed a name, so a later
        // reconnect pins the same machine even if DNS changes.
        var dialHost = host;
        if (!IPAddress.TryParse(host, out _))
        {
            try
            {
                var addrs = await Dns.GetHostAddressesAsync(host, System.Net.Sockets.AddressFamily.InterNetwork, ct);
                if (addrs.Length > 0) dialHost = addrs[0].ToString();
            }
            catch { /* keep the name */ }
        }

        return new DiscoveryResponse
        {
            Kind = "response",
            ServerId = status.ServerId,
            RoomName = string.IsNullOrWhiteSpace(status.RoomName) ? "Classroom" : status.RoomName,
            Host = dialHost,
            Port = port,
            HubPath = Protocol.HubPath,
            ProtocolVersion = status.ProtocolVersion,
        };
    }
}
