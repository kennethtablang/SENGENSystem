using ClassInsight.Agent;
using ClassInsight.Agent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;

// The student agent is a headless background worker. It runs in one of three modes:
//
//  * Windows service (deployed on a lab PC, FR-AGENT-02): starts at boot under LocalSystem and runs
//    the SessionSupervisor, which launches this same exe in the student's console session (see
//    below) and keeps it alive. Services live in session 0 and can't see the student's desktop,
//    hence the split.
//  * Session agent (`--session-agent`, launched by the supervisor): the real monitor — discovery,
//    registration, telemetry and screen streaming — running as SYSTEM on the student's desktop.
//  * Console (`dotnet run`, development): the monitor directly in the current session, Ctrl+C to stop.
//
// No mode needs per-machine configuration: the agent finds the server itself (UDP discovery)
// and joins with no code.
if (!OperatingSystem.IsWindows())
{
    // The agent captures the Windows desktop via GDI; other platforms are teacher-only.
    Console.Error.WriteLine("ClassInsight.Agent runs on Windows lab machines only.");
    return;
}

// Physical-pixel coordinates for capture on scaled (125 %/150 %) displays. Must precede any
// window/DC use.
NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

var sessionAgent = args.Contains(Program.SessionAgentSwitch, StringComparer.OrdinalIgnoreCase);
var asService = !sessionAgent && WindowsServiceHelpers.IsWindowsService();

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args.Where(a => !a.Equals(Program.SessionAgentSwitch, StringComparison.OrdinalIgnoreCase)).ToArray(),
    // Under the SCM (or launched by it) the working directory is System32; anchor config to the exe.
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.AddWindowsService(o => o.ServiceName = "ClassInsight Agent");

builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

// Neither the service nor its session agent has a console to read; mirror diagnostics into the
// Windows Event Log so a lab admin can see the agent's health in Event Viewer.
if (asService || sessionAgent)
    builder.Logging.AddEventLog(o => o.SourceName = "ClassInsight Agent");

if (asService)
    builder.Services.AddHostedService<SessionSupervisor>();
else
    builder.Services.AddHostedService<AgentWorker>();

await builder.Build().RunAsync();

public partial class Program
{
    /// <summary>Command-line switch the service supervisor uses to start the in-session monitor.</summary>
    public const string SessionAgentSwitch = "--session-agent";
}
