using ClassInsight.Server.Hubs;
using ClassInsight.Server.Services;
using ClassInsight.Shared;

var builder = WebApplication.CreateBuilder(args);

// Bind Kestrel to the configured ClassInsight port on all interfaces so lab machines on
// the LAN can reach the hub. (Discovery hands clients the resolved LAN address.)
var port = builder.Configuration.GetValue<int?>("ClassInsight:Port") ?? Protocol.DefaultServerPort;
builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(port));

builder.Services.AddSignalR(options =>
{
    // Screen SDP offers and telemetry can be larger than the default 32 KB cap.
    options.MaximumReceiveMessageSize = 1024 * 1024; // 1 MB
});

builder.Services.AddSingleton<ServerIdentity>();
builder.Services.AddSingleton<RoomRegistry>();
builder.Services.AddSingleton<TeacherAuthThrottle>();
builder.Services.AddHostedService<DiscoveryBeaconService>();

var app = builder.Build();

// Force ServerIdentity to resolve now so its startup banner (and any generated join code)
// prints before the first client connects.
_ = app.Services.GetRequiredService<ServerIdentity>();

app.MapHub<ClassroomHub>(Protocol.HubPath);
app.MapGet("/", () => Results.Ok(new { service = "ClassInsight", status = "ok" }));

app.Run();
