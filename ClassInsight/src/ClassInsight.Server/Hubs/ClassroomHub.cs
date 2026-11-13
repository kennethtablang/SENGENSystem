using ClassInsight.Server.Services;
using ClassInsight.Shared;
using ClassInsight.Shared.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace ClassInsight.Server.Hubs;

/// <summary>
/// The single coordination hub for the classroom. It does three jobs:
///   1. Roster: students register and are projected to teachers in real time.
///   2. Signalling relay: it shuttles opaque WebRTC SDP/ICE between an authorised
///      teacher↔student pair without ever parsing the media (FR-SEC-04).
///   3. Telemetry fan-out: student samples are pushed to every connected teacher.
///
/// Students join with no code by design (FR-SEC-01, lab deployment). Teachers are gated:
/// open mode admits them freely (FR-SEC-03); otherwise a correct join code is required and
/// bad attempts are throttled (FR-SEC-02). Reconstructed from the shared protocol and both
/// client implementations after the original was lost.
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
        // Prefer the real socket address the server sees over whatever the agent guessed.
        var ip = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(ip))
            info = info with { IpAddress = ip };

        var entry = _room.AddOrUpdateStudent(Context.ConnectionId, info);
        _logger.LogInformation("Student joined: {Name} ({Ip})", info.MachineName, info.IpAddress);

        // Tell every teacher about the newcomer, and hand the student the current room size.
        var snapshot = entry.ToSnapshot();
        await Clients.Clients(_room.TeacherConnectionIds).SendAsync(Protocol.ClientMethods.StudentJoined, snapshot);
        await BroadcastRosterToTeachers();
    }

    /// <summary>
    /// A teacher device registers. Fails closed: unless the server runs open, a valid join
    /// code must be presented or the call throws <see cref="HubException"/> (which the
    /// teacher app reads as "needs code").
    /// </summary>
    public async Task RegisterTeacher(TeacherInfo info)
    {
        if (!_identity.AllowOpenTeacherMode)
        {
            var key = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? Context.ConnectionId;

            if (_throttle.GetLockout(key) is { } wait)
                throw new HubException($"Too many attempts. Try again in {Math.Ceiling(wait.TotalSeconds)}s.");

            var presented = info.JoinCode?.Trim() ?? string.Empty;
            if (!string.Equals(presented, _identity.TeacherCode, StringComparison.Ordinal))
            {
                _throttle.RegisterFailure(key);
                _logger.LogWarning("Teacher registration rejected (bad code) from {Key}", key);
                throw new HubException("Invalid join code.");
            }

            _throttle.RegisterSuccess(key);
        }

        _room.AddOrUpdateTeacher(Context.ConnectionId, info);
        _logger.LogInformation("Teacher joined: {Device} ({Platform})", info.DeviceName, info.Platform);

        // Give the newly-admitted teacher the full roster immediately.
        await Clients.Caller.SendAsync(Protocol.ClientMethods.RosterUpdated, _room.BuildRoster());
    }

    /// <summary>Explicit roster pull (teachers normally rely on the pushed snapshot).</summary>
    public Task<RosterSnapshot> GetRoster() => Task.FromResult(_room.BuildRoster());

    // ---- streaming control (teacher -> student) ---------------------------

    /// <summary>
    /// A teacher asks to view a student's screen. We relay the request to that student,
    /// tagging it with the requesting teacher's connection id so the agent knows whom to
    /// offer to. The agent then produces the WebRTC offer.
    /// </summary>
    public async Task RequestStream(StreamRequest request)
    {
        if (!_room.IsTeacher(Context.ConnectionId)) return;             // only teachers may view
        if (!_room.IsStudent(request.StudentConnectionId)) return;      // target must exist

        await Clients.Client(request.StudentConnectionId)
            .SendAsync(Protocol.ClientMethods.StreamRequested, Context.ConnectionId, request);

        if (_room.SetStudentState(request.StudentConnectionId, StudentState.Streaming))
            await Clients.Clients(_room.TeacherConnectionIds)
                .SendAsync(Protocol.ClientMethods.StudentStateChanged, request.StudentConnectionId, StudentState.Streaming);
    }

    /// <summary>A teacher stops viewing a student; the agent tears down that peer.</summary>
    public async Task StopStream(string studentConnectionId)
    {
        if (!_room.IsTeacher(Context.ConnectionId)) return;
        if (!_room.IsStudent(studentConnectionId)) return;

        await Clients.Client(studentConnectionId)
            .SendAsync(Protocol.ClientMethods.StreamStopRequested, Context.ConnectionId);

        if (_room.SetStudentState(studentConnectionId, StudentState.Online))
            await Clients.Clients(_room.TeacherConnectionIds)
                .SendAsync(Protocol.ClientMethods.StudentStateChanged, studentConnectionId, StudentState.Online);
    }

    // ---- signalling relay (opaque SDP/ICE) --------------------------------

    /// <summary>Student (sender) → teacher: the SDP offer.</summary>
    public Task SendOffer(SdpMessage message) => RelaySdp(message, Protocol.ClientMethods.ReceiveOffer);

    /// <summary>Teacher (viewer) → student: the SDP answer.</summary>
    public Task SendAnswer(SdpMessage message) => RelaySdp(message, Protocol.ClientMethods.ReceiveAnswer);

    private async Task RelaySdp(SdpMessage message, string clientMethod)
    {
        if (string.IsNullOrEmpty(message.TargetConnectionId)) return;
        var relayed = message with { FromConnectionId = Context.ConnectionId };
        await Clients.Client(message.TargetConnectionId).SendAsync(clientMethod, relayed);
    }

    /// <summary>Relay a single ICE candidate to the addressed peer (either direction).</summary>
    public async Task SendIceCandidate(IceCandidateMessage message)
    {
        if (string.IsNullOrEmpty(message.TargetConnectionId)) return;
        var relayed = message with { FromConnectionId = Context.ConnectionId };
        await Clients.Client(message.TargetConnectionId)
            .SendAsync(Protocol.ClientMethods.ReceiveIceCandidate, relayed);
    }

    // ---- telemetry (student -> all teachers) ------------------------------

    public async Task PushTelemetry(TelemetrySnapshot telemetry)
    {
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
        else if (_room.RemoveTeacher(id))
        {
            _logger.LogInformation("Teacher left: {Id}", id);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private Task BroadcastRosterToTeachers() =>
        Clients.Clients(_room.TeacherConnectionIds).SendAsync(Protocol.ClientMethods.RosterUpdated, _room.BuildRoster());
}
