using ClassInsight.Agent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The student agent is a headless background worker. On a managed lab PC it is intended to
// run as a Windows service / startup task so it auto-connects the moment the machine is on
// (FR-AGENT-01). It needs no configuration: it finds the server itself and joins with no code.
var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

if (OperatingSystem.IsWindows())
{
    builder.Services.AddHostedService<AgentWorker>();
}
else
{
    // The agent captures the Windows desktop via GDI; other platforms are teacher-only.
    Console.Error.WriteLine("ClassInsight.Agent runs on Windows lab machines only.");
    return;
}

await builder.Build().RunAsync();
