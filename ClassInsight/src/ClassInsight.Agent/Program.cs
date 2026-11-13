using ClassInsight.Agent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;

// The student agent is a headless background worker. On a managed lab PC it is deployed as a
// Windows service that starts automatically at boot (FR-AGENT-02), so it auto-connects the
// moment the machine is on, with no student action and no per-machine configuration: it finds
// the server itself (UDP discovery) and joins with no code.
//
// The same executable also runs as a plain console app for development — AddWindowsService()
// is a no-op unless the process was actually launched by the Windows Service Control Manager,
// so `dotnet run` still gives an interactive, Ctrl+C-stoppable agent.
var builder = Host.CreateApplicationBuilder(args);

// When the SCM starts us, the working directory is C:\Windows\System32; anchor the content
// root to the executable so config/relative paths resolve the same as in console mode.
builder.Services.AddWindowsService(o => o.ServiceName = "ClassInsight Agent");

builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

if (OperatingSystem.IsWindows())
{
    // Under the SCM there is no console to read; mirror diagnostics into the Windows Event Log
    // so a lab admin can see the agent's health in Event Viewer (Applications → ClassInsight).
    if (WindowsServiceHelpers.IsWindowsService())
    {
        builder.Logging.AddEventLog(o => o.SourceName = "ClassInsight Agent");
    }

    builder.Services.AddHostedService<AgentWorker>();
}
else
{
    // The agent captures the Windows desktop via GDI; other platforms are teacher-only.
    Console.Error.WriteLine("ClassInsight.Agent runs on Windows lab machines only.");
    return;
}

await builder.Build().RunAsync();
