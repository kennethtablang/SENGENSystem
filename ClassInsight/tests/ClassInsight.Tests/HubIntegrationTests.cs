using System.Collections.Concurrent;
using System.Net.Http.Json;
using ClassInsight.Server.Hubs;
using ClassInsight.Shared;
using ClassInsight.Shared.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace ClassInsight.Tests;

/// <summary>
/// In-process SignalR hub flows (plan §10): register → request stream → pair-gated signalling
/// relay (positive and injection cases) → teardown → telemetry fan-out. The real server pipeline
/// runs on a TestServer; discovery is switched off so tests don't touch UDP 47800.
/// </summary>
public sealed class ServerFactory : WebApplicationFactory<ClassroomHub>
{
    private readonly Dictionary<string, string?> _settings;

    public ServerFactory(params (string Key, string? Value)[] settings)
    {
        _settings = new()
        {
            ["ClassInsight:DiscoveryEnabled"] = "false",
            ["ClassInsight:AllowOpenTeacherMode"] = "true",
            ["ClassInsight:RoomName"] = "Test Lab",
        };
        foreach (var (k, v) in settings) _settings[k] = v;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var (k, v) in _settings) builder.UseSetting(k, v);
    }

    public async Task<Peer> ConnectAsync()
    {
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, Protocol.HubPath), o =>
            {
                // Real WebSocket transport over the in-memory TestServer (as clients use on the LAN),
                // so a client stopping is seen by the server immediately.
                o.Transports = HttpTransportType.WebSockets;
                o.HttpMessageHandlerFactory = _ => Server.CreateHandler();   // negotiate
                o.WebSocketFactory = async (ctx, ct) => await Server.CreateWebSocketClient().ConnectAsync(ctx.Uri, ct);
            })
            .Build();
        var peer = new Peer(hub);
        await hub.StartAsync();
        return peer;
    }
}

/// <summary>A test client that records every server→client message it receives.</summary>
public sealed class Peer : IAsyncDisposable
{
    private readonly ConcurrentQueue<(string Method, object?[] Args)> _inbox = new();
    private readonly SemaphoreSlim _arrived = new(0);

    public HubConnection Hub { get; }
    public string Id => Hub.ConnectionId!;

    public Peer(HubConnection hub)
    {
        Hub = hub;
        Record<RosterSnapshot>(Protocol.ClientMethods.RosterUpdated);
        Record<StudentSnapshot>(Protocol.ClientMethods.StudentJoined);
        Record<string>(Protocol.ClientMethods.StudentLeft);
        Record<string, StudentState>(Protocol.ClientMethods.StudentStateChanged);
        Record<string, StreamRequest>(Protocol.ClientMethods.StreamRequested);
        Record<string>(Protocol.ClientMethods.StreamStopRequested);
        Record<SdpMessage>(Protocol.ClientMethods.ReceiveOffer);
        Record<SdpMessage>(Protocol.ClientMethods.ReceiveAnswer);
        Record<IceCandidateMessage>(Protocol.ClientMethods.ReceiveIceCandidate);
        Record<string, TelemetrySnapshot>(Protocol.ClientMethods.TelemetryUpdated);
    }

    private void Record<T>(string method) => Hub.On<T>(method, a => Push(method, a));
    private void Record<T1, T2>(string method) => Hub.On<T1, T2>(method, (a, b) => Push(method, a, b));

    private void Push(string method, params object?[] args)
    {
        _inbox.Enqueue((method, args));
        _arrived.Release();
    }

    /// <summary>Wait for a message of <paramref name="method"/> matching <paramref name="match"/>.</summary>
    public async Task<object?[]> WaitForAsync(string method, Func<object?[], bool>? match = null, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            foreach (var m in _inbox)
                if (m.Method == method && (match?.Invoke(m.Args) ?? true))
                    return m.Args;

            var left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero || !await _arrived.WaitAsync(left))
                throw new TimeoutException($"No {method} within {timeoutMs} ms.");
        }
    }

    /// <summary>Every <paramref name="method"/> message received so far.</summary>
    public IReadOnlyList<object?[]> All(string method) => _inbox.Where(m => m.Method == method).Select(m => m.Args).ToArray();

    /// <summary>Assert no <paramref name="method"/> arrives within a grace period.</summary>
    public async Task AssertNoneAsync(string method, int graceMs = 600)
    {
        await Task.Delay(graceMs);
        Assert.DoesNotContain(_inbox, m => m.Method == method);
    }

    public Task RegisterStudentAsync(string name = "PC1") =>
        Hub.InvokeAsync(Protocol.ServerMethods.RegisterStudent, new StudentInfo
        {
            MachineId = Guid.NewGuid().ToString("N"),
            MachineName = name,
            Displays = [new DisplayInfo { Index = 0, Width = 1920, Height = 1080, IsPrimary = true }],
        });

    public Task RegisterTeacherAsync(string? code = null) =>
        Hub.InvokeAsync(Protocol.ServerMethods.RegisterTeacher, new TeacherInfo { DeviceName = "T", JoinCode = code });

    public async ValueTask DisposeAsync() => await Hub.DisposeAsync();
}

public class HubIntegrationTests : IAsyncLifetime
{
    private readonly ServerFactory _factory = new(("ClassInsight:IceServers:0:Urls", "turn:10.0.0.9:3478"));

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Teacher_sees_registered_students_and_telemetry()
    {
        await using var student = await _factory.ConnectAsync();
        await student.RegisterStudentAsync("LAB-PC-01");
        await using var teacher = await _factory.ConnectAsync();
        await teacher.RegisterTeacherAsync();

        var roster = (RosterSnapshot)(await teacher.WaitForAsync(Protocol.ClientMethods.RosterUpdated))[0]!;
        var entry = Assert.Single(roster.Students);
        Assert.Equal("LAB-PC-01", entry.MachineName);
        Assert.Equal(1920, Assert.Single(entry.Displays).Width);

        await student.Hub.InvokeAsync(Protocol.ServerMethods.PushTelemetry, new TelemetrySnapshot { ActiveUrl = "example.org" });
        var tel = await teacher.WaitForAsync(Protocol.ClientMethods.TelemetryUpdated);
        Assert.Equal(student.Id, tel[0]);
        Assert.Equal("example.org", ((TelemetrySnapshot)tel[1]!).ActiveUrl);
    }

    [Fact]
    public async Task Signalling_is_relayed_only_within_an_authorised_pair()
    {
        await using var student = await _factory.ConnectAsync();
        await student.RegisterStudentAsync();
        await using var teacher = await _factory.ConnectAsync();
        await teacher.RegisterTeacherAsync();
        await using var intruder = await _factory.ConnectAsync();      // a second, unpaired student
        await intruder.RegisterStudentAsync("PC-X");

        // Before any RequestStream nothing is relayed.
        await student.Hub.InvokeAsync(Protocol.ServerMethods.SendOffer, new SdpMessage { TargetConnectionId = teacher.Id, Sdp = "early" });
        await teacher.AssertNoneAsync(Protocol.ClientMethods.ReceiveOffer);

        await teacher.Hub.InvokeAsync(Protocol.ServerMethods.RequestStream,
            new StreamRequest { StudentConnectionId = student.Id, Quality = StreamQuality.Focus, DisplayIndex = 1 });
        var req = await student.WaitForAsync(Protocol.ClientMethods.StreamRequested);
        Assert.Equal(teacher.Id, req[0]);
        Assert.Equal(1, ((StreamRequest)req[1]!).DisplayIndex);
        await teacher.WaitForAsync(Protocol.ClientMethods.StudentStateChanged, a => (StudentState)a[1]! == StudentState.Streaming);

        // Paired student → teacher offer, teacher → student answer, ICE both ways.
        await student.Hub.InvokeAsync(Protocol.ServerMethods.SendOffer, new SdpMessage { TargetConnectionId = teacher.Id, Sdp = "offer-sdp", FromConnectionId = "spoofed" });
        var offer = (SdpMessage)(await teacher.WaitForAsync(Protocol.ClientMethods.ReceiveOffer))[0]!;
        Assert.Equal(student.Id, offer.FromConnectionId);    // server stamps the real sender
        Assert.Equal("offer-sdp", offer.Sdp);

        await teacher.Hub.InvokeAsync(Protocol.ServerMethods.SendAnswer, new SdpMessage { TargetConnectionId = student.Id, Sdp = "answer-sdp" });
        Assert.Equal("answer-sdp", ((SdpMessage)(await student.WaitForAsync(Protocol.ClientMethods.ReceiveAnswer))[0]!).Sdp);

        await student.Hub.InvokeAsync(Protocol.ServerMethods.SendIceCandidate, new IceCandidateMessage { TargetConnectionId = teacher.Id, Candidate = "c1" });
        await teacher.WaitForAsync(Protocol.ClientMethods.ReceiveIceCandidate);
        await teacher.Hub.InvokeAsync(Protocol.ServerMethods.SendIceCandidate, new IceCandidateMessage { TargetConnectionId = student.Id, Candidate = "c2" });
        await student.WaitForAsync(Protocol.ClientMethods.ReceiveIceCandidate);

        // Injection: the unpaired student can't reach the teacher, nor the teacher's student.
        await intruder.Hub.InvokeAsync(Protocol.ServerMethods.SendOffer, new SdpMessage { TargetConnectionId = teacher.Id, Sdp = "evil" });
        await intruder.Hub.InvokeAsync(Protocol.ServerMethods.SendIceCandidate, new IceCandidateMessage { TargetConnectionId = teacher.Id, Candidate = "evil" });
        await intruder.Hub.InvokeAsync(Protocol.ServerMethods.SendAnswer, new SdpMessage { TargetConnectionId = student.Id, Sdp = "evil" });
        await Task.Delay(800);
        Assert.Equal(["offer-sdp"], teacher.All(Protocol.ClientMethods.ReceiveOffer).Select(a => ((SdpMessage)a[0]!).Sdp));
        Assert.Equal(["c1"], teacher.All(Protocol.ClientMethods.ReceiveIceCandidate).Select(a => ((IceCandidateMessage)a[0]!).Candidate));
        Assert.Equal(["answer-sdp"], student.All(Protocol.ClientMethods.ReceiveAnswer).Select(a => ((SdpMessage)a[0]!).Sdp));
    }

    [Fact]
    public async Task Student_returns_to_online_only_when_the_last_viewer_stops()
    {
        await using var student = await _factory.ConnectAsync();
        await student.RegisterStudentAsync();
        await using var t1 = await _factory.ConnectAsync();
        await t1.RegisterTeacherAsync();
        await using var t2 = await _factory.ConnectAsync();
        await t2.RegisterTeacherAsync();

        await t1.Hub.InvokeAsync(Protocol.ServerMethods.RequestStream, new StreamRequest { StudentConnectionId = student.Id });
        await t2.Hub.InvokeAsync(Protocol.ServerMethods.RequestStream, new StreamRequest { StudentConnectionId = student.Id });
        await student.WaitForAsync(Protocol.ClientMethods.StreamRequested, a => (string)a[0]! == t2.Id);

        await t1.Hub.InvokeAsync(Protocol.ServerMethods.StopStream, student.Id);
        await student.WaitForAsync(Protocol.ClientMethods.StreamStopRequested, a => (string)a[0]! == t1.Id);
        await Assert.ThrowsAsync<TimeoutException>(() => t2.WaitForAsync(Protocol.ClientMethods.StudentStateChanged,
            a => (StudentState)a[1]! == StudentState.Online, 500));

        // A teacher dropping off counts as stopping: the agent is told, and the student goes Online.
        var t2Id = t2.Id;   // ConnectionId is cleared once the client stops
        await t2.Hub.StopAsync();
        await student.WaitForAsync(Protocol.ClientMethods.StreamStopRequested, a => (string)a[0]! == t2Id);
        var roster = await t1.Hub.InvokeAsync<RosterSnapshot>(Protocol.ServerMethods.GetRoster);
        Assert.Equal(StudentState.Online, Assert.Single(roster.Students).State);
    }

    [Fact]
    public async Task Stop_without_a_pair_is_ignored_and_roles_are_exclusive()
    {
        await using var student = await _factory.ConnectAsync();
        await student.RegisterStudentAsync();
        await using var teacher = await _factory.ConnectAsync();
        await teacher.RegisterTeacherAsync();

        await teacher.Hub.InvokeAsync(Protocol.ServerMethods.StopStream, student.Id);
        await student.AssertNoneAsync(Protocol.ClientMethods.StreamStopRequested);

        // A student connection can't become a teacher (and so can't authorise itself a pair).
        await Assert.ThrowsAsync<HubException>(() => student.RegisterTeacherAsync());
        await Assert.ThrowsAsync<HubException>(() => student.Hub.InvokeAsync<RosterSnapshot>(Protocol.ServerMethods.GetRoster));
        // Students asking to view do nothing.
        await student.Hub.InvokeAsync(Protocol.ServerMethods.RequestStream, new StreamRequest { StudentConnectionId = student.Id });
        await student.AssertNoneAsync(Protocol.ClientMethods.StreamRequested);
    }

    [Fact]
    public async Task Malformed_targets_are_ignored_not_errors()
    {
        await using var teacher = await _factory.ConnectAsync();
        await teacher.RegisterTeacherAsync();
        // Null / empty ids must be dropped quietly, not surface as server faults.
        await teacher.Hub.InvokeAsync(Protocol.ServerMethods.RequestStream, new StreamRequest { StudentConnectionId = null! });
        await teacher.Hub.InvokeAsync(Protocol.ServerMethods.StopStream, (string?)null);
        await teacher.Hub.InvokeAsync(Protocol.ServerMethods.SendAnswer, new SdpMessage { TargetConnectionId = null! });
        await teacher.Hub.InvokeAsync(Protocol.ServerMethods.SendIceCandidate, new IceCandidateMessage { TargetConnectionId = "" });
    }

    [Fact]
    public async Task Protocol_mismatch_is_rejected()
    {
        await using var peer = await _factory.ConnectAsync();
        var ex = await Assert.ThrowsAsync<HubException>(() =>
            peer.Hub.InvokeAsync(Protocol.ServerMethods.RegisterStudent, new StudentInfo { ProtocolVersion = 999 }));
        Assert.Contains("Protocol version mismatch", ex.Message);
    }

    [Fact]
    public async Task Ice_servers_only_for_registered_clients_and_status_endpoint()
    {
        await using var anon = await _factory.ConnectAsync();
        Assert.Empty(await anon.Hub.InvokeAsync<IReadOnlyList<IceServerConfig>>(Protocol.ServerMethods.GetIceServers));

        await using var student = await _factory.ConnectAsync();
        await student.RegisterStudentAsync();
        var ice = await student.Hub.InvokeAsync<IReadOnlyList<IceServerConfig>>(Protocol.ServerMethods.GetIceServers);
        Assert.Equal("turn:10.0.0.9:3478", Assert.Single(ice).Urls);

        var status = await _factory.CreateClient().GetFromJsonAsync<ServerStatus>(Protocol.StatusPath);
        Assert.Equal("Test Lab", status!.RoomName);
        Assert.Equal(Protocol.Version, status.ProtocolVersion);
        Assert.Equal(1, status.Students);
        Assert.False(string.IsNullOrEmpty(status.ServerId));
    }
}

public class ClosedModeTests : IAsyncLifetime
{
    private readonly ServerFactory _factory = new(
        ("ClassInsight:AllowOpenTeacherMode", "false"),
        ("ClassInsight:TeacherCode", "ABCD-EFGH-JKMNP"));

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Teacher_needs_the_code_and_students_never_do()
    {
        await using var student = await _factory.ConnectAsync();
        await student.RegisterStudentAsync();   // no code, no login (FR-SEC-01)

        await using var teacher = await _factory.ConnectAsync();
        await Assert.ThrowsAsync<HubException>(() => teacher.RegisterTeacherAsync());
        await Assert.ThrowsAsync<HubException>(() => teacher.RegisterTeacherAsync("ABCD-EFGH-JKMNQ"));

        // Not registered → can't view.
        await teacher.Hub.InvokeAsync(Protocol.ServerMethods.RequestStream, new StreamRequest { StudentConnectionId = student.Id });
        await student.AssertNoneAsync(Protocol.ClientMethods.StreamRequested);

        await teacher.RegisterTeacherAsync("abcd efgh jkmnp");   // separators/case don't matter
        await teacher.WaitForAsync(Protocol.ClientMethods.RosterUpdated);
    }

    [Fact]
    public async Task Repeated_bad_codes_lock_the_source_out()
    {
        await using var teacher = await _factory.ConnectAsync();
        HubException? last = null;
        for (var i = 0; i < 8; i++)
            last = await Assert.ThrowsAsync<HubException>(() => teacher.RegisterTeacherAsync("WRONG"));
        Assert.Contains("Too many attempts", last!.Message);

        // Even the right code is refused while locked out.
        var locked = await Assert.ThrowsAsync<HubException>(() => teacher.RegisterTeacherAsync("ABCD-EFGH-JKMNP"));
        Assert.Contains("Too many attempts", locked.Message);
    }
}
