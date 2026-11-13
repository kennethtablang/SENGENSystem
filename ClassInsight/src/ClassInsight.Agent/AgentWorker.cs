using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using ClassInsight.Agent.Services;
using ClassInsight.Shared;
using ClassInsight.Shared.Contracts;
using ClassInsight.Shared.Discovery;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClassInsight.Agent;

/// <summary>
/// The agent's lifecycle: discover the classroom server on the LAN with no configuration,
/// register as a student (no code, no login — FR-SEC-01), stream live telemetry on a fixed
/// cadence, and serve WebRTC screen streams on demand. It reconnects and re-registers on
/// its own if the server restarts or the link drops.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AgentWorker : BackgroundService
{
    private const string AgentVersion = "0.1.0";
    private static readonly TimeSpan TelemetryInterval = TimeSpan.FromSeconds(1);   // ≥ 1 Hz (NFR-PERF-03)
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);

    private readonly ILogger<AgentWorker> _logger;
    private readonly ILoggerFactory _loggerFactory;

    // One screen sender per watching teacher, keyed by the teacher's connection id.
    private readonly ConcurrentDictionary<string, ScreenStreamSender> _senders = new();

    private readonly StudentInfo _info;
    private readonly SystemTelemetryCollector _telemetry;

    public AgentWorker(ILogger<AgentWorker> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _info = BuildStudentInfo();
        _telemetry = new SystemTelemetryCollector(_info.MachineId);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("ClassInsight agent starting on {Machine} ({User}).", _info.MachineName, _info.UserName);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Session ended with an error; retrying.");
            }

            await TearDownSendersAsync();

            if (!ct.IsCancellationRequested)
                await SafeDelay(ReconnectDelay, ct);
        }
    }

    private async Task RunSessionAsync(CancellationToken ct)
    {
        var server = await DiscoverAsync(ct);
        if (server is null) return;

        _logger.LogInformation("Found \"{Room}\" at {Url}.", server.RoomName, server.HubUrl);

        await using var hub = new HubConnectionBuilder()
            .WithUrl(server.HubUrl)
            .Build();

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };

        RegisterSignalingHandlers(hub);

        await hub.StartAsync(ct);
        await hub.InvokeAsync(Protocol.ServerMethods.RegisterStudent, _info, ct);
        _logger.LogInformation("Registered with the classroom server.");

        await TelemetryLoopAsync(hub, closed.Task, ct);
    }

    private async Task<DiscoveryResponse?> DiscoverAsync(CancellationToken ct)
    {
        var discovery = new LanDiscoveryClient("student");
        while (!ct.IsCancellationRequested)
        {
            var server = await discovery.FindServerAsync(TimeSpan.FromSeconds(3), ct);
            if (server is not null) return server;
            _logger.LogDebug("No server yet; still searching…");
        }
        return null;
    }

    private void RegisterSignalingHandlers(HubConnection hub)
    {
        // A teacher asked to watch this student. The server tags the request with the
        // requesting teacher's connection id so we know whom to offer to.
        hub.On<string, StreamRequest>(Protocol.ClientMethods.StreamRequested, async (teacherId, request) =>
        {
            await StartSenderAsync(hub, teacherId, request.Quality);
        });

        hub.On<string>(Protocol.ClientMethods.StreamStopRequested, async teacherId =>
        {
            if (_senders.TryRemove(teacherId, out var sender))
                await sender.DisposeAsync();
        });

        hub.On<SdpMessage>(Protocol.ClientMethods.ReceiveAnswer, answer =>
        {
            if (_senders.TryGetValue(answer.FromConnectionId, out var sender))
                sender.HandleAnswer(answer);
        });

        hub.On<IceCandidateMessage>(Protocol.ClientMethods.ReceiveIceCandidate, candidate =>
        {
            if (_senders.TryGetValue(candidate.FromConnectionId, out var sender))
                sender.HandleIceCandidate(candidate);
        });
    }

    private async Task StartSenderAsync(HubConnection hub, string teacherId, StreamQuality quality)
    {
        // Replace any existing stream to this teacher (e.g. a quality change / re-request).
        if (_senders.TryRemove(teacherId, out var existing))
            await existing.DisposeAsync();

        var sender = new ScreenStreamSender(hub, teacherId, quality, _loggerFactory.CreateLogger<ScreenStreamSender>());
        _senders[teacherId] = sender;
        try
        {
            await sender.StartAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not start stream to {Teacher}.", teacherId);
            if (_senders.TryRemove(teacherId, out var s)) await s.DisposeAsync();
        }
    }

    private async Task TelemetryLoopAsync(HubConnection hub, Task closed, CancellationToken ct)
    {
        // Prime deltas so the first published sample is meaningful.
        _telemetry.Collect();

        using var timer = new PeriodicTimer(TelemetryInterval);
        while (!ct.IsCancellationRequested)
        {
            var tick = timer.WaitForNextTickAsync(ct).AsTask();
            var done = await Task.WhenAny(tick, closed);
            if (done == closed) return;                 // connection dropped; caller rediscovers
            if (!await tick) return;

            if (hub.State != HubConnectionState.Connected) continue;

            try
            {
                var sample = _telemetry.Collect();
                await hub.SendAsync(Protocol.ServerMethods.PushTelemetry, sample, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Telemetry push failed.");
            }
        }
    }

    private async Task TearDownSendersAsync()
    {
        foreach (var key in _senders.Keys.ToArray())
            if (_senders.TryRemove(key, out var sender))
                await sender.DisposeAsync();
    }

    private static async Task SafeDelay(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); }
        catch (OperationCanceledException) { }
    }

    private static StudentInfo BuildStudentInfo()
    {
        int w = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN));
        int h = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN));

        return new StudentInfo
        {
            MachineId = StableMachineId(),
            MachineName = Environment.MachineName,
            UserName = SafeUserName(),
            OsVersion = Environment.OSVersion.VersionString,
            IpAddress = string.Empty,     // the server fills this from the real socket address
            ScreenWidth = w,
            ScreenHeight = h,
            AgentVersion = AgentVersion,
        };
    }

    private static string? SafeUserName()
    {
        try { return Environment.UserName; }
        catch { return null; }
    }

    /// <summary>
    /// A stable per-machine id so a student keeps its tile across reconnects: a hash of the
    /// machine name and the first physical NIC address. Not a secret — just an identity key.
    /// </summary>
    private static string StableMachineId()
    {
        var seed = Environment.MachineName;
        try
        {
            var mac = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .Select(n => n.GetPhysicalAddress().ToString())
                .FirstOrDefault(s => !string.IsNullOrEmpty(s));
            if (mac is not null) seed += "|" + mac;
        }
        catch { /* fall back to machine name alone */ }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(hash, 0, 8);   // 16 hex chars, plenty for a room
    }
}
