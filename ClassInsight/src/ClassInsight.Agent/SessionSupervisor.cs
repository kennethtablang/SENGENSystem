using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using ClassInsight.Agent.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static ClassInsight.Agent.Services.NativeMethods;

namespace ClassInsight.Agent;

/// <summary>
/// What the Windows service actually runs (FR-AGENT-02/07).
///
/// A service lives in session 0, which has no access to the student's desktop: screen capture,
/// the foreground window and UI Automation all see an empty, isolated desktop there. So the
/// service doesn't do the monitoring itself. It supervises a copy of this same executable,
/// started with <see cref="Program.SessionAgentSwitch"/> <b>inside the active console session</b>
/// on <c>winsta0\default</c>, using the service's own LocalSystem token re-targeted at that
/// session. That child runs <see cref="AgentWorker"/> and sees exactly what the student sees.
///
/// Because the child runs as SYSTEM, a standard student can't end it from Task Manager; if it
/// exits anyway it is relaunched within seconds, and it is moved to the new session when the
/// console user changes (log off / fast user switch). The child is placed in a kill-on-close job
/// object, so it can never outlive the service (no orphaned duplicates after a service restart).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SessionSupervisor : BackgroundService
{
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RestartBackoffMax = TimeSpan.FromSeconds(30);

    private readonly ILogger<SessionSupervisor> _logger;
    private IntPtr _job;
    private IntPtr _child;
    private uint _childSession = NoSession;

    public SessionSupervisor(ILogger<SessionSupervisor> logger) => _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _job = CreateKillOnCloseJob();
        var backoff = TimeSpan.FromSeconds(1);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var session = WTSGetActiveConsoleSessionId();

                if (_child != IntPtr.Zero && (!IsRunning(_child) || _childSession != session))
                {
                    if (_childSession != session)
                        _logger.LogInformation("Console session changed ({Old} → {New}); moving the agent.", _childSession, session);
                    else
                        _logger.LogWarning("Session agent exited; relaunching.");
                    StopChild();
                }

                if (_child == IntPtr.Zero && session != NoSession && session != 0)
                {
                    try
                    {
                        LaunchInSession(session);
                        backoff = TimeSpan.FromSeconds(1);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Could not start the agent in session {Session}; retrying in {Delay}s.", session, backoff.TotalSeconds);
                        await Delay(backoff, ct);
                        backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, RestartBackoffMax.Ticks));
                        continue;
                    }
                }

                await Delay(Poll, ct);
            }
        }
        finally
        {
            StopChild();
            if (_job != IntPtr.Zero) { CloseHandle(_job); _job = IntPtr.Zero; }
        }
    }

    private void LaunchInSession(uint session)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.");

        IntPtr own = IntPtr.Zero, dup = IntPtr.Zero, env = IntPtr.Zero;
        PROCESS_INFORMATION pi = default;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(),
                    TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ASSIGN_PRIMARY | TOKEN_ADJUST_SESSIONID | TOKEN_ADJUST_DEFAULT,
                    out own))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken");

            if (!DuplicateTokenEx(own, MAXIMUM_ALLOWED, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out dup))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx");

            // Same LocalSystem identity, but bound to the student's session (needs SeTcbPrivilege,
            // which LocalSystem holds).
            var sid = session;
            if (!SetTokenInformation(dup, TokenSessionId, ref sid, sizeof(uint)))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetTokenInformation(TokenSessionId)");

            CreateEnvironmentBlock(out env, dup, false);

            var si = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                lpDesktop = @"winsta0\default",
            };
            var cmd = new StringBuilder($"\"{exe}\" {Program.SessionAgentSwitch}");

            if (!CreateProcessAsUser(dup, exe, cmd, IntPtr.Zero, IntPtr.Zero, false,
                    CREATE_SUSPENDED | CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT,
                    env, Path.GetDirectoryName(exe), ref si, out pi))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessAsUser");

            // Join the kill-on-close job before the child runs a single instruction.
            if (_job != IntPtr.Zero && !AssignProcessToJobObject(_job, pi.hProcess))
                _logger.LogWarning("Could not place the session agent in the service job ({Error}).", Marshal.GetLastWin32Error());

            ResumeThread(pi.hThread);

            _child = pi.hProcess;
            pi.hProcess = IntPtr.Zero;   // ownership moved to _child
            _childSession = session;
            _logger.LogInformation("Started the session agent (pid {Pid}) in console session {Session}.", pi.dwProcessId, session);
        }
        finally
        {
            if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
            if (pi.hProcess != IntPtr.Zero) { TerminateProcess(pi.hProcess, 1); CloseHandle(pi.hProcess); }
            if (env != IntPtr.Zero) DestroyEnvironmentBlock(env);
            if (dup != IntPtr.Zero) CloseHandle(dup);
            if (own != IntPtr.Zero) CloseHandle(own);
        }
    }

    private void StopChild()
    {
        if (_child == IntPtr.Zero) return;
        if (IsRunning(_child)) TerminateProcess(_child, 0);
        CloseHandle(_child);
        _child = IntPtr.Zero;
        _childSession = NoSession;
    }

    private static bool IsRunning(IntPtr process) =>
        GetExitCodeProcess(process, out var code) && code == STILL_ACTIVE;

    private IntPtr CreateKillOnCloseJob()
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero)
        {
            _logger.LogWarning("Could not create a job object; a crashed service may leave its agent running until restart.");
            return IntPtr.Zero;
        }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION { LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE },
        };
        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
            _logger.LogWarning("Could not configure the job object ({Error}).", Marshal.GetLastWin32Error());
        return job;
    }

    private static async Task Delay(TimeSpan t, CancellationToken ct)
    {
        try { await Task.Delay(t, ct); } catch (OperationCanceledException) { }
    }
}
