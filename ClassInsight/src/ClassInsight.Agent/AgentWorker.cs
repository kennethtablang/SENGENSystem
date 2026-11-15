using System.Collections.Concurrent;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using ClassInsight.Agent.Services;
using ClassInsight.Shared;
using ClassInsight.Shared.Contracts;
using ClassInsight.Shared.Discovery;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClassInsight.Agent;

/// <summary>
/// The agent's lifecycle: discover the classroom server on the LAN with no configuration,
/// register as a student (no code, no login — FR-SEC-01), stream live telemetry on a fixed
/// cadence, and serve WebRTC screen streams on demand. It reconnects and re-registers on
/// its own if the server restarts or the link drops (FR-DISC-05).
///
/// Optional IT settings (<c>ClassInsight</c> section of appsettings.json — none are needed on a
/// normal lab LAN): <c>RoomName</c> pins the agent to one room when several share a network
/// (FR-SRV-05); <c>ServerAddress</c> is a manual <c>host[:port]</c> fallback for networks that
/// block broadcast (NFR-COMPAT-02); <c>ReportBrowserActivity</c> toggles the browser-host
/// telemetry (FR-AGENT-06).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AgentWorker : BackgroundService
{
    public const string AgentVersion = "0.2.0";
    private static readonly TimeSpan TelemetryInterval = TimeSpan.FromSeconds(1);   // ≥ 1 Hz (NFR-PERF-03)
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);

    private readonly ILogger<AgentWorker> _logger;
    private readonly ILoggerFactory _loggerFactory;

    // One screen sender per watching teacher, keyed by the teacher's connection id (FR-AGENT-08).
    private readonly ConcurrentDictionary<string, ScreenStreamSender> _senders = new();
    // Serialises start/stop per teacher so a quick quality switch can't race its own teardown.
    private readonly SemaphoreSlim _senderGate = new(1, 1);

    private readonly string? _roomName;
    private readonly string? _serverAddress;
    private readonly string _machineId;
    private readonly SystemTelemetryCollector _telemetry;
    private readonly BrowserActivityProbe? _browser;
    private readonly ViewingIndicator? _indicator;

    private IReadOnlyList<IceServerConfig> _iceServers = [];

    public AgentWorker(ILogger<AgentWorker> logger, ILoggerFactory loggerFactory, IConfiguration configuration)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;

        var section = configuration.GetSection("ClassInsight");
        _roomName = NullIfBlank(section["RoomName"]);
        ScreenCapture.DisableDxgi = string.Equals(section["CaptureBackend"], "GDI", StringComparison.OrdinalIgnoreCase);
        _serverAddress = NullIfBlank(section["ServerAddress"]);

        if (section.GetValue<bool?>("ReportBrowserActivity") ?? true)
            _browser = new BrowserActivityProbe(loggerFactory.CreateLogger<BrowserActivityProbe>());

        // On-screen notice while a teacher is viewing (FR-SEC-05). On by default; an institution
        // whose policy doesn't require notice can turn it off.
        if (section.GetValue<bool?>("ShowViewingIndicator") ?? true)
            _indicator = new ViewingIndicator(loggerFactory.CreateLogger<ViewingIndicator>());

        _machineId = InstallId.GetOrCreate(logger);
        _telemetry = new SystemTelemetryCollector(_machineId, _browser);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var info = BuildStudentInfo();
        _logger.LogInformation("ClassInsight agent {Version} starting on {Machine} ({User}), {Displays} display(s){Room}.",
            AgentVersion, info.MachineName, info.UserName ?? "no user", info.Displays.Count,
            _roomName is null ? "" : $", room \"{_roomName}\"");

        try
        {
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
        finally
        {
            await TearDownSendersAsync();
            _browser?.Dispose();
            _indicator?.Dispose();
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
        // Re-read identity each session: the signed-in user or display layout may have changed.
        await hub.InvokeAsync(Protocol.ServerMethods.RegisterStudent, BuildStudentInfo(), ct);
        _logger.LogInformation("Registered with the classroom server.");

        try
        {
            _iceServers = await hub.InvokeAsync<IReadOnlyList<IceServerConfig>>(Protocol.ServerMethods.GetIceServers, ct) ?? [];
        }
        catch (Exception ex)
        {
            _iceServers = [];
            _logger.LogDebug(ex, "Server offered no ICE servers.");
        }

        await TelemetryLoopAsync(hub, closed.Task, ct);
    }

    private async Task<DiscoveryResponse?> DiscoverAsync(CancellationToken ct)
    {
        var discovery = new LanDiscoveryClient("student");
        while (!ct.IsCancellationRequested)
        {
            var server = await discovery.FindServerAsync(TimeSpan.FromSeconds(3), ct, _roomName);
            if (server is not null) return server;

            // Broadcast blocked or no beacon heard: try the address IT configured, if any.
            if (_serverAddress is not null)
            {
                var manual = await ManualServer.ResolveAsync(_serverAddress, TimeSpan.FromSeconds(3), ct);
                if (manual is not null && LanDiscoveryClient.MatchesRoom(manual, _roomName)) return manual;
            }

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
            _logger.LogInformation("Teacher {Teacher} requested {Quality} stream of display {Display}.",
                teacherId, request.Quality, request.DisplayIndex);
            await StartSenderAsync(hub, teacherId, request);
        });

        hub.On<string>(Protocol.ClientMethods.StreamStopRequested, async teacherId =>
        {
            await _senderGate.WaitAsync();
            try
            {
                if (_senders.TryRemove(teacherId, out var sender))
                {
                    await sender.DisposeAsync();
                    _logger.LogInformation("Stopped stream to teacher {Teacher}; {Count} viewer(s) left.", teacherId, _senders.Count);
                }
                UpdateIndicator();
            }
            finally
            {
                _senderGate.Release();
            }
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

    private async Task StartSenderAsync(HubConnection hub, string teacherId, StreamRequest request)
    {
        var displays = DisplayEnumerator.GetDisplays();
        var display = displays.ElementAtOrDefault(request.DisplayIndex);
        if (display.Width == 0) display = displays[0];   // out of range → primary

        await _senderGate.WaitAsync();
        try
        {
            // Replace any existing stream to this teacher (quality or display change / re-request).
            if (_senders.TryRemove(teacherId, out var existing))
                await existing.DisposeAsync();

            var sender = new ScreenStreamSender(hub, teacherId, request.Quality, display, _iceServers,
                _loggerFactory.CreateLogger<ScreenStreamSender>());
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
            UpdateIndicator();
        }
        finally
        {
            _senderGate.Release();
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
        await _senderGate.WaitAsync();
        try
        {
            foreach (var key in _senders.Keys.ToArray())
                if (_senders.TryRemove(key, out var sender))
                    await sender.DisposeAsync();
            UpdateIndicator();
        }
        finally
        {
            _senderGate.Release();
        }
    }

    private void UpdateIndicator() => _indicator?.SetViewing(!_senders.IsEmpty);

    private static async Task SafeDelay(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); }
        catch (OperationCanceledException) { }
    }

    private StudentInfo BuildStudentInfo()
    {
        var displays = DisplayEnumerator.GetDisplays();
        var primary = displays[0];

        return new StudentInfo
        {
            MachineId = _machineId,
            MachineName = Environment.MachineName,
            UserName = SignedInUser(),
            OsVersion = Environment.OSVersion.VersionString,
            IpAddress = string.Empty,     // the server fills this from the real socket address
            ScreenWidth = primary.Width,
            ScreenHeight = primary.Height,
            Displays = displays.Select(d => d.Info).ToArray(),
            AgentVersion = AgentVersion,
        };
    }

    /// <summary>
    /// The student signed in to this session. Under the service supervisor the agent runs as
    /// SYSTEM inside the student's session, so the process user ("SYSTEM") is wrong — ask the
    /// session instead, falling back to the process user in console/dev mode.
    /// </summary>
    private static string? SignedInUser()
    {
        try
        {
            if (NativeMethods.ProcessIdToSessionId((uint)Environment.ProcessId, out var session)
                && NativeMethods.GetSessionUserName(session) is { } user)
                return user;
        }
        catch { /* fall through */ }

        try
        {
            var name = Environment.UserName;
            return string.Equals(name, "SYSTEM", StringComparison.OrdinalIgnoreCase) ? null : name;
        }
        catch
        {
            return null;
        }
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>
/// The agent's <see cref="StudentInfo.MachineId"/>: a random 128-bit identifier generated once per
/// install and persisted, so a student keeps its tile across reconnects (FR-SEC-07). It is not
/// derived from the machine name, MAC or any hardware attribute, so it can't be predicted or
/// forged by someone who merely knows the PC. Stored under %ProgramData%\ClassInsight (shared by
/// the service and console runs); falls back to the user profile if that isn't writable.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class InstallId
{
    private const string FileName = "agent-id";

    public static string GetOrCreate(ILogger logger)
    {
        foreach (var root in new[] { Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.LocalApplicationData })
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(root), "ClassInsight");
                var path = Path.Combine(dir, FileName);

                if (File.Exists(path))
                {
                    var existing = File.ReadAllText(path).Trim();
                    if (existing.Length == 32 && existing.All(Uri.IsHexDigit)) return existing;
                }

                Directory.CreateDirectory(dir);
                var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                File.WriteAllText(path, id);
                return id;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Could not persist install id under {Root}.", root);
            }
        }

        // Nowhere writable: still random, just not stable across restarts.
        logger.LogWarning("Could not persist the agent install id; this machine will get a new tile after a restart.");
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    }
}
