using StealthEye.Contract;
using ModelContextProtocol.Server;
using StealthEye.Runtime;
using StealthEye.Tools;

var builder = WebApplication.CreateBuilder(args);

var publicContract = EyeContractCatalog.Load();
var eyeLiveTool = EyeLiveMcp.CreateTool(publicContract);

builder.Services.AddWindowsService(options => options.ServiceName = "StealthEye");
var urls = Environment.GetEnvironmentVariable("EYE_URLS")
    ?? builder.Configuration["Eye:Urls"]
    ?? "http://127.0.0.1:37931";
builder.WebHost.UseUrls(urls);

builder.Services.AddSingleton<ProcessRunner>();
builder.Services.AddSingleton<JobStore>();
builder.Services.AddSingleton<ArtifactStore>();
builder.Services.AddSingleton<TriggerStore>();
builder.Services.AddSingleton<TriggerBroker>();
builder.Services.AddSingleton<EngineSupervisor>();
builder.Services.AddSingleton(sp => new SessionWorkerManager(
    sp.GetRequiredService<EngineSupervisor>(),
    WorkerRpcMethods.CurrentProtocolVersion));
builder.Services.AddSingleton<DesktopWindowStore>();
builder.Services.AddSingleton<DesktopObservationService>();
builder.Services.AddSingleton<UiaElementStore>();
builder.Services.AddSingleton<UiaQueryService>();
builder.Services.AddSingleton<UiaActionService>();
builder.Services.AddSingleton<UiaTriggerSource>();
builder.Services.AddSingleton<JobManager>();
builder.Services.AddSingleton<EyeDispatcher>();
builder.Services.AddSingleton<EyeLiveSnapshotService>();
builder.Services.AddSingleton<EyeLiveTool>();
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<EyeTool>()
    .WithTools(new[] { eyeLiveTool })
    .WithResources<EyeLiveResource>();

var app = builder.Build();
_ = app.Services.GetRequiredService<JobStore>();
_ = app.Services.GetRequiredService<ArtifactStore>();
await app.Services.GetRequiredService<TriggerBroker>().InitializeAsync();
var engineSupervisor = app.Services.GetRequiredService<EngineSupervisor>();
await engineSupervisor.InitializeAsync();

app.MapGet("/health", () => Results.Json(new
{
    product = "StealthEye",
    executable = "eye",
    version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
    pid = Environment.ProcessId
}));

app.MapMcp("/mcp");

await app.RunAsync();
