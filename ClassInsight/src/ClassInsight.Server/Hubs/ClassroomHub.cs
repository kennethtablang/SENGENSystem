using ClassInsight.Server.Services;
using ClassInsight.Shared;
using ClassInsight.Shared.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace ClassInsight.Server.Hubs;

/// <summary>
/// The single coordination hub for the classroom. It does three jobs:
///   1. Roster: students register and are projected to teachers in real time.
///   2. Signalling relay: it shuttles opaque WebRTC SDP/ICE between an <b>authorised</b>
///      teacher↔student pair — and nobody else — without ever parsing the media (FR-SEC-04).
///   3. Telemetry fan-out: student samples are pushed to every connected teacher.
///
/// Students join with no code by design (FR-SEC-01, lab deployment). Teachers are gated:
/// open mode admits them freely (FR-SEC-03); otherwise a correct join code is required and
/// bad attempts are throttled (FR-SEC-02/06). A connection holds exactly one role.
/// </summary>
public sealed class ClassroomHub : Hub
{
    private readonly RoomRegistry _room;
    private readonly ServerIdentity _identity;
    private readonly TeacherAuthThrottle _throttle;
    private readonly ILogger<ClassroomHub> _logger;

    public ClassroomHub(
        RoomRegistry room,
        ServerIdentity identity,
        TeacherAuthThrottle throttle,
        ILogger<ClassroomHub> logger)
    {
        _room = room;
        _identity = identity;
        _throttle = throttle;
        _logger = logger;
    }

    // ---- registration -----------------------------------------------------

    /// <summary>A student agent announces itself. No code, no login (FR-SEC-01).</summary>
    public async Task RegisterStudent(StudentInfo info)
    {
        RejectIfIncompatible(info.ProtocolVersion);
        if (_room.IsTeacher(Context.ConnectionId))
            throw new HubException("This connection is already registered as a teacher.");

        // Prefer the real socket address the server sees over whatever the agent guessed.
        var ip = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(ip))
            info = info with { IpAddress = ip };

        // Bound what an agent can make every teacher render.
        info = info with { Displays = info.Displays.Take(16).ToArray() };

        var entry = _room.AddOrUpdateStudent(Context.ConnectionId, info);
        _logger.LogInformation("Student joined: {Name} ({Ip})", info.MachineName, info.IpAddress);

        await Clients.Clients(_room.TeacherConnectionIds).SendAsync(Protocol.ClientMethods.StudentJoined, entry.ToSnapshot());
        await BroadcastRosterToTeachers();
    }

    /// <summary>
    /// A teacher device registers. Fails closed: unless the server runs open, a valid join
    /// code must be presented or the call throws <see cref="HubException"/> (which the
    /// teacher app reads as "needs code").
    /// </summary>
    public async Task RegisterTeacher(TeacherInfo info)
    {
        RejectIfIncompatible(info.ProtocolVersion);
        if (_room.IsStudent(Context.ConnectionId))
            throw new HubException("This connection is already registered as a student.");

        if (!_identity.AllowOpenTeacherMode)
        {
            var key = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? Context.ConnectionId;

            if (_throttle.GetLockout(key) is { } wait)
                throw new HubException($"Too many attempts. Try again in {Math.Ceiling(wait.TotalSeconds)}s.");

            if (!_identity.VerifyTeacherCode(info.JoinCode))
            {
                _throttle.RegisterFailure(key);
                _logger.LogWarning("Teacher registration rejected (bad code) from {Key}", key);
                throw new HubException("Invalid join code.");
            }

            _throttle.RegisterSuccess(key);
        }

        // Never keep the code around in the roster.
        _room.AddOrUpdateTeacher(Context.ConnectionId, info with { JoinCode = null });
        _logger.LogInformation("Teacher joined: {Device} ({Platform})", info.DeviceName, info.Platform);

        // Give the newly-admitted teacher the full roster immediately.
        await Clients.Caller.SendAsync(Protocol.ClientMethods.RosterUpdated, _room.BuildRoster());
    }

    /// <summary>Explicit roster pull (teachers normally rely on the pushed snapshot).</summary>
    public RosterSnapshot GetRoster()
    {
        if (!_room.IsTeacher(Context.ConnectionId))
            throw new HubException("Only teachers can read the roster.");
        return _room.BuildRoster();
    }

    /// <summary>
    /// STUN/TURN servers for WebRTC (relay fallback, P4). Only registered clients get them,
    /// because TURN entries carry credentials.
    /// </summary>
    public IReadOnlyList<IceServerConfig> GetIceServers()
    {
        var id = Context.ConnectionId;
        return _room.IsTeacher(id) || _room.IsStudent(id) ? _identity.IceServers : [];
    }

    // ---- streaming control (teacher -> student) ---------------------------

    /// <summary>
    /// A teacher asks to view a student's screen. This is the only way a viewing pair is
    /// authorised (FR-STREAM-05). We relay the request to the student tagged with the teacher's
    /// connection id; the agent then produces the WebRTC offer. A repeat request (e.g. a quality
    /// or display change) just re-sends — the agent replaces its stream to that teacher.
    /// </summary>
    public async Task RequestStream(StreamRequest request)
    {
        var teacher = Context.ConnectionId;
        if (string.IsNullOrEmpty(request?.StudentConnectionId)) return;
        if (!_room.AuthorizePair(teacher, request.StudentConnectionId)) return;   // not a teacher, or no such student

        await Clients.Client(request.StudentConnectionId)
            .SendAsync(Protocol.ClientMethods.StreamRequested, teacher, request);

        await SetStateAsync(request.StudentConnectionId, StudentState.Streaming);
    }

    /// <summary>
    /// A teacher stops viewing a student; the agent tears down that peer. The student returns to
    /// <see cref="StudentState.Online"/> only once no teacher is still watching (FR-STREAM-06).
    /// </summary>
    public async Task StopStream(string studentConnectionId)
    {
        var teacher = Context.ConnectionId;
        if (string.IsNullOrEmpty(studentConnectionId)) return;
        if (!_room.RevokePair(teacher, studentConnectionId)) return;

        await Clients.Client(studentConnectionId)
            .SendAsync(Protocol.ClientMethods.StreamStopRequested, teacher);

        if (_room.ViewerCount(studentConnectionId) == 0)
            await SetStateAsync(studentConnectionId, StudentState.Online);
    }

    // ---- signalling relay (opaque SDP/ICE, pair-gated) --------------------

    /// <summary>Student (sender) → teacher: the SDP offer. Only to a teacher authorised to view this student.</summary>
    public Task SendOffer(SdpMessage message)
    {
        var student = Context.ConnectionId;
        if (string.IsNullOrEmpty(message?.TargetConnectionId)) return Task.CompletedTask;
        if (!_room.IsPaired(message.TargetConnectionId, student)) return Dropped("offer", message.TargetConnectionId);
        return Clients.Client(message.TargetConnectionId).SendAsync(Protocol.ClientMethods.ReceiveOffer,
            message with { FromConnectionId = student, Type = "offer" });
    }

    /// <summary>Teacher (viewer) → student: the SDP answer. Only to a student this teacher is authorised to view.</summary>
    public Task SendAnswer(SdpMessage message)
    {
        var teacher = Context.ConnectionId;
        if (string.IsNullOrEmpty(message?.TargetConnectionId)) return Task.CompletedTask;
        if (!_room.IsPaired(teacher, message.TargetConnectionId)) return Dropped("answer", message.TargetConnectionId);
        return Clients.Client(message.TargetConnectionId).SendAsync(Protocol.ClientMethods.ReceiveAnswer,
            message with { FromConnectionId = teacher, Type = "answer" });
    }

    /// <summary>Relay a single ICE candidate to the other end of an authorised pair (either direction).</summary>
    public Task SendIceCandidate(IceCandidateMessage message)
    {
        var from = Context.ConnectionId;
        var target = message?.TargetConnectionId;
        if (message is null || string.IsNullOrEmpty(target)) return Task.CompletedTask;
        var allowed = (_room.IsTeacher(from) && _room.IsPaired(from, target))
                      || (_room.IsStudent(from) && _room.IsPaired(target, from));
        if (!allowed) return Dropped("ICE candidate", target);

        return Clients.Client(target).SendAsync(Protocol.ClientMethods.ReceiveIceCandidate,
            message with { FromConnectionId = from });
    }

    private Task Dropped(string what, string target)
    {
        _logger.LogDebug("Dropped unauthorised {What} from {From} to {Target}.", what, Context.ConnectionId, target);
        return Task.CompletedTask;
    }

    // ---- telemetry (student -> all teachers) ------------------------------

    public async Task PushTelemetry(TelemetrySnapshot telemetry)
    {
        if (telemetry is null) return;
        var entry = _room.ApplyTelemetry(Context.ConnectionId, telemetry);
        if (entry is null) return;   // not a registered student

        await Clients.Clients(_room.TeacherConnectionIds)
            .SendAsync(Protocol.ClientMethods.TelemetryUpdated, Context.ConnectionId, telemetry);
    }

    // ---- lifecycle --------------------------------------------------------

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var id = Context.ConnectionId;

        if (_room.RemoveStudent(id, out var student))
        {
            _logger.LogInformation("Student left: {Name}", student?.Info.MachineName);
            await Clients.Clients(_room.TeacherConnectionIds).SendAsync(Protocol.ClientMethods.StudentLeft, id);
            await BroadcastRosterToTeachers();
        }
        else if (_room.RemoveTeacher(id, out var watched))
        {
            _logger.LogInformation("Teacher left: {Id}", id);

            // Tell each agent this teacher was watching to stop encoding for it right away,
            // instead of waiting for WebRTC to notice the dead peer (FR-STREAM-06, NFR-REL-03).
            foreach (var s in watched)
            {
                await Clients.Client(s).SendAsync(Protocol.ClientMethods.StreamStopRequested, id);
                if (_room.ViewerCount(s) == 0)
                    await SetStateAsync(s, StudentState.Online);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task SetStateAsync(string student, StudentState state)
    {
        if (_room.SetStudentState(student, state))
            await Clients.Clients(_room.TeacherConnectionIds)
                .SendAsync(Protocol.ClientMethods.StudentStateChanged, student, state);
    }

    private Task BroadcastRosterToTeachers() =>
        Clients.Clients(_room.TeacherConnectionIds).SendAsync(Protocol.ClientMethods.RosterUpdated, _room.BuildRoster());

    /// <summary>
    /// Reject a client whose wire-protocol version doesn't match this server's (FR-DISC-06).
    /// Throwing <see cref="HubException"/> fails the registration call on the client; nothing
    /// is added to the roster, so an incompatible build can't half-join.
    /// </summary>
    private void RejectIfIncompatible(int clientProtocolVersion)
    {
        if (clientProtocolVersion != Protocol.Version)
        {
            _logger.LogWarning("Rejected client with protocol v{Client} (server v{Server}).",
                clientProtocolVersion, Protocol.Version);
            throw new HubException(
                $"Protocol version mismatch: client v{clientProtocolVersion}, server v{Protocol.Version}. Update to a matching build.");
        }
    }
}
