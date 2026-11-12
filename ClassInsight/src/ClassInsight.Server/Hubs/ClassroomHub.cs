using ClassInsight.Server.Services;
using ClassInsight.Shared;
using ClassInsight.Shared.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace ClassInsight.Server.Hubs;

/// <summary>
/// The single real-time hub every student agent and teacher app connects to.
/// Responsibilities:
///   1. Roster management  — who is in the room right now.
///   2. WebRTC signalling   — relay SDP/ICE between a viewing teacher and a student.
///   3. Telemetry fan-out   — forward each student's live samples to every teacher.
///
/// The server is a coordinator/relay only. Screen video flows peer-to-peer over
/// WebRTC; the hub never touches the media.
/// </summary>
public sealed class ClassroomHub : Hub
{
    private readonly RoomRegistry _registry;
    private readonly ServerIdentity _identity;
    private readonly TeacherAuthThrottle _throttle;
    private readonly ILogger<ClassroomHub> _logger;

    public ClassroomHub(
        RoomRegistry registry,
        ServerIdentity identity,
        TeacherAuthThrottle throttle,
        ILogger<ClassroomHub> logger)
    {
        _registry = registry;
        _identity = identity;
        _throttle = throttle;
        _logger = logger;
    }

    // ---- Registration ---------------------------------------------------

    public async Task RegisterStudent(StudentInfo info)
    {
        _registry.AddOrUpdateStudent(Context.ConnectionId, info);
        _logger.LogInformation("Student registered: {Machine} ({Ip}) conn={Conn}",
            info.MachineName, info.IpAddress, Context.ConnectionId);

        if (_registry.TryGetStudent(Context.ConnectionId, out var entry))
        {
            var snapshot = _registry.ProjectStudent(entry);
            await TeachersExcept(null).SendAsync(Protocol.ClientMethods.StudentJoined, snapshot);
        }
        await BroadcastRosterToTeachers();
    }

    public async Task RegisterTeacher(TeacherInfo info)
    {
        // Gate the teacher role behind the pairing code. Students are never affected —
        // only a device claiming teacher privileges must present it. Failed attempts
        // are throttled/locked out so a short code cannot be brute-forced online.
        if (_identity.RequiresTeacherCode)
        {
            var key = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString()
                      ?? Context.ConnectionId;

            if (_throttle.IsLockedOut(key))
            {
                _logger.LogWarning("Teacher registration throttled (locked out) key={Key}", key);
                throw new HubException("Too many attempts. Please wait a moment and try again.");
            }

            if (!FixedTimeEquals(info.JoinCode, _identity.TeacherCode))
            {
                _throttle.RecordFailure(key);
                _logger.LogWarning("Rejected teacher registration (bad join code) conn={Conn} key={Key}",
                    Context.ConnectionId, key);
                throw new HubException("Invalid teacher join code.");
            }

            _throttle.RecordSuccess(key);
        }

        _registry.AddOrUpdateTeacher(Context.ConnectionId, info);
        _logger.LogInformation("Teacher registered: {Device}/{Platform} conn={Conn}",
            info.DeviceName, info.Platform, Context.ConnectionId);

        // Hand the newly-connected teacher the current roster immediately.
        await Clients.Caller.SendAsync(Protocol.ClientMethods.RosterUpdated, _registry.BuildRoster());
    }

    public RosterSnapshot GetRoster()
    {
        // The roster exposes machine names, users and IPs — teachers only.
        if (!_registry.IsTeacher(Context.ConnectionId))
            throw new HubException("Not authorized.");
        return _registry.BuildRoster();
    }

    // ---- WebRTC signalling (teacher viewer <-> student sender) ----------

    /// <summary>Teacher asks to view a student. The student will respond with an SDP offer.</summary>
    public async Task RequestStream(StreamRequest request)
    {
        if (!_registry.IsTeacher(Context.ConnectionId)) return;
        if (string.IsNullOrEmpty(request.StudentConnectionId)) return;
        // Only address real, currently-connected students.
        if (!_registry.TryGetStudent(request.StudentConnectionId, out _)) return;

        // Record the authorised pair so the subsequent SDP/ICE relay is permitted.
        _registry.AuthorizeView(Context.ConnectionId, request.StudentConnectionId);

        await Clients.Client(request.StudentConnectionId)
            .SendAsync(Protocol.ClientMethods.StreamRequested, Context.ConnectionId, request.Quality);
    }

    /// <summary>Teacher stops viewing a student; tell the agent it can tear down that peer.</summary>
    public async Task StopStream(string studentConnectionId)
    {
        if (!_registry.IsTeacher(Context.ConnectionId)) return;

        _registry.RevokeView(Context.ConnectionId, studentConnectionId);
        await Clients.Client(studentConnectionId)
            .SendAsync(Protocol.ClientMethods.StreamStopRequested, Context.ConnectionId);

        // If no teacher is watching this student any more, drop it back to Online.
        if (!_registry.StudentHasViewer(studentConnectionId))
        {
            _registry.SetStudentState(studentConnectionId, StudentState.Online);
            await BroadcastStudentState(studentConnectionId, StudentState.Online);
        }
    }

    public async Task SendOffer(SdpMessage message)
    {
        if (!IsAuthorizedSignallingPair(Context.ConnectionId, message.TargetConnectionId)) return;

        await Clients.Client(message.TargetConnectionId).SendAsync(
            Protocol.ClientMethods.ReceiveOffer,
            message with { FromConnectionId = Context.ConnectionId });

        _registry.SetStudentState(Context.ConnectionId, StudentState.Streaming);
        await BroadcastStudentState(Context.ConnectionId, StudentState.Streaming);
    }

    public Task SendAnswer(SdpMessage message)
    {
        if (!IsAuthorizedSignallingPair(Context.ConnectionId, message.TargetConnectionId))
            return Task.CompletedTask;

        return Clients.Client(message.TargetConnectionId).SendAsync(
            Protocol.ClientMethods.ReceiveAnswer,
            message with { FromConnectionId = Context.ConnectionId });
    }

    public Task SendIceCandidate(IceCandidateMessage message)
    {
        if (!IsAuthorizedSignallingPair(Context.ConnectionId, message.TargetConnectionId))
            return Task.CompletedTask;

        return Clients.Client(message.TargetConnectionId).SendAsync(
            Protocol.ClientMethods.ReceiveIceCandidate,
            message with { FromConnectionId = Context.ConnectionId });
    }

    // ---- Telemetry ------------------------------------------------------

    public async Task PushTelemetry(TelemetrySnapshot snapshot)
    {
        _registry.UpdateTelemetry(Context.ConnectionId, snapshot);
        // Tag the sample with its connection id so teachers can route it to a tile.
        await TeachersExcept(null).SendAsync(
            Protocol.ClientMethods.TelemetryUpdated, Context.ConnectionId, snapshot);
    }

    // ---- Lifecycle ------------------------------------------------------

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var role = _registry.Remove(Context.ConnectionId);
        if (role == ClientRole.Student)
        {
            _logger.LogInformation("Student disconnected conn={Conn}", Context.ConnectionId);
            await TeachersExcept(null).SendAsync(
                Protocol.ClientMethods.StudentLeft, Context.ConnectionId);
            await BroadcastRosterToTeachers();
        }
        else if (role == ClientRole.Teacher)
        {
            _logger.LogInformation("Teacher disconnected conn={Conn}", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    // ---- Helpers --------------------------------------------------------

    /// <summary>
    /// True only when {sender, target} is a teacher↔student pair that the server
    /// itself authorised via RequestStream, in either direction. Everything else is
    /// dropped, so no client can relay signalling to a connection of its choosing.
    /// </summary>
    private bool IsAuthorizedSignallingPair(string sender, string target)
    {
        if (string.IsNullOrEmpty(target)) return false;

        // sender = teacher, target = student
        if (_registry.IsTeacher(sender) && _registry.TryGetStudent(target, out _))
            return _registry.IsViewAuthorized(sender, target);

        // sender = student, target = teacher
        if (_registry.TryGetStudent(sender, out _) && _registry.IsTeacher(target))
            return _registry.IsViewAuthorized(target, sender);

        return false;
    }

    /// <summary>Length-constant comparison so a bad join code can't be timed.</summary>
    private static bool FixedTimeEquals(string? provided, string? expected)
    {
        if (expected is null) return false;
        var a = System.Text.Encoding.UTF8.GetBytes(provided ?? string.Empty);
        var b = System.Text.Encoding.UTF8.GetBytes(expected);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }

    private IClientProxy TeachersExcept(string? exceptConnectionId)
    {
        var ids = _registry.TeacherConnectionIds();
        if (exceptConnectionId is not null)
            ids = ids.Where(id => id != exceptConnectionId).ToArray();
        return Clients.Clients(ids);
    }

    private Task BroadcastRosterToTeachers() =>
        TeachersExcept(null).SendAsync(Protocol.ClientMethods.RosterUpdated, _registry.BuildRoster());

    private Task BroadcastStudentState(string connectionId, StudentState state) =>
        TeachersExcept(null).SendAsync(
            Protocol.ClientMethods.StudentStateChanged, connectionId, state);
}
