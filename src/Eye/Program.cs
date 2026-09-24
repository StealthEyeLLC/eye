using System.Text.Json;
using ModelContextProtocol.Server;
using StealthEye.Contract;
using StealthEye.Runtime;
using StealthEye.Tools;

if (args.Length > 0 && args[0] is "inventory" or "doctor" or "torture-test")
{
    if (args[0] == "torture-test")
    {
        var torture = await EyeFoundationTortureTest.RunAsync();
        Console.WriteLine(JsonSerializer.Serialize(torture, new JsonSerializerOptions { WriteIndented = true }));
        Environment.ExitCode = torture.Overall == DiagnosticStates.Fail ? 1 : 0;
        return;
    }

    var jobs = new JobStore();
    var actions = new ActionJournalStore(jobs);
    var artifacts = new ArtifactStore(jobs);
    var diagnostics = new EyeDiagnostics(jobs, actions, artifacts, EyeContractCatalog.Load());
    var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

    if (args[0] == "inventory")
    {
        Console.WriteLine(JsonSerializer.Serialize(diagnostics.Inventory(), jsonOptions));
        return;
    }

    var report = await diagnostics.DoctorAsync();
    Console.WriteLine(JsonSerializer.Serialize(report, jsonOptions));
    Environment.ExitCode = report.Overall == DiagnosticStates.Fail ? 1 : 0;
    return;
}

InteractiveTaskProcessLauncher.CleanupStaleOwnedResidue();

var builder = WebApplication.CreateBuilder(args);

var publicContract = EyeContractCatalog.Load();
var modelTools = EyeGeneratedMcp.CreateModelTools(publicContract);
var eyeLiveTool = EyeLiveMcp.CreateTool(publicContract);
var servedTools = modelTools.Append(eyeLiveTool).ToArray();

builder.Services.AddWindowsService(options => options.ServiceName = "StealthEye");
var urls = Environment.GetEnvironmentVariable("EYE_URLS")
    ?? builder.Configuration["Eye:Urls"]
    ?? "http://127.0.0.1:37931";
builder.WebHost.UseUrls(urls);

builder.Services.AddSingleton(publicContract);
builder.Services.AddSingleton<ProcessRunner>();
builder.Services.AddSingleton<JobStore>();
builder.Services.AddSingleton<ActionJournalStore>();
builder.Services.AddSingleton<IPostconditionInspector, FilePostconditionInspector>();
builder.Services.AddSingleton<IPostconditionInspector, CommandPostconditionInspector>();
builder.Services.AddSingleton<PostconditionInspectorRegistry>();
builder.Services.AddSingleton<ActionReconciler>();
builder.Services.AddSingleton<ConsequentialActionRunner>();
builder.Services.AddSingleton<ArtifactStore>();
builder.Services.AddSingleton<MissionBlackboardStore>();
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
builder.Services.AddSingleton<BrowserSessionManager>();
builder.Services.AddSingleton<BrowserTargetStore>();
builder.Services.AddSingleton<BrowserObservationService>();
builder.Services.AddSingleton<BrowserControlService>();
builder.Services.AddSingleton<UiaTriggerSource>();
builder.Services.AddSingleton<JobManager>();
builder.Services.AddSingleton<EyeDispatcher>();
builder.Services.AddSingleton<EyeTool>();
builder.Services.AddSingleton<EyeLiveSnapshotService>();
builder.Services.AddSingleton<EyeLiveTool>();
builder.Services
    .AddMcpServer(options => options.ServerInstructions = publicContract.Manifest.ServerInstructions)
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools(servedTools)
    .WithResources<EyeLiveResource>();

var app = builder.Build();
_ = app.Services.GetRequiredService<JobStore>();
var actionJournal = app.Services.GetRequiredService<ActionJournalStore>();
actionJournal.RecoverAfterHostRestart();
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
