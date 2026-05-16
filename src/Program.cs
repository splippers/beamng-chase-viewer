using BeamQuest.Core;
using BeamQuest.Protocol;
using BeamQuest.XR;
using BeamQuest.Rendering;
using Microsoft.Extensions.Logging;

// Android entry point — same pattern as SLQuest
var logFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
var log        = logFactory.CreateLogger("Main");

log.LogInformation("BeamQuest Viewer starting");

var xr     = new XRSession(logFactory.CreateLogger<XRSession>());
var vulkan = new VulkanContext();
vulkan.Init(xr);

await using var app = new BeamApplication(logFactory, xr, vulkan);

// ── Select source from command-line args / config ─────────────────────────
// On Quest 3 the args come from an intent extra; default = UDP source on :37420
string sourceType = Environment.GetEnvironmentVariable("BQ_SOURCE") ?? "udp";
string host       = Environment.GetEnvironmentVariable("BQ_HOST")   ?? "192.168.1.1";
int    port       = int.TryParse(Environment.GetEnvironmentVariable("BQ_PORT"), out var p) ? p : 37420;
string replayPath = Environment.GetEnvironmentVariable("BQ_REPLAY") ?? "";

IVehicleDataSource source = sourceType switch
{
    "ws"       => new BeamMPWebSocketSource(host, port, logFactory.CreateLogger<BeamMPWebSocketSource>()),
    "outgauge" => new OutGaugeSource(4444,           logFactory.CreateLogger<OutGaugeSource>()),
    "replay"   => new ReplaySource(replayPath,       logFactory.CreateLogger<ReplaySource>()),
    _          => new BeamNGUDPSource(port,          logFactory.CreateLogger<BeamNGUDPSource>()),
};

await app.ConnectAsync(source);
await app.RunAsync();
