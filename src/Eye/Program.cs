using ModelContextProtocol.Server;
using StealthEye.Runtime;
using StealthEye.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWindowsService(options => options.ServiceName = "StealthEye");
var urls = Environment.GetEnvironmentVariable("EYE_URLS")
    ?? builder.Configuration["Eye:Urls"]
    ?? "http://127.0.0.1:37931";
builder.WebHost.UseUrls(urls);

builder.Services.AddSingleton<ProcessRunner>();
builder.Services.AddSingleton<JobStore>();
builder.Services.AddSingleton<JobManager>();
builder.Services.AddSingleton<EyeDispatcher>();
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<EyeTool>();

var app = builder.Build();
_ = app.Services.GetRequiredService<JobStore>();

app.MapGet("/health", () => Results.Json(new
{
    product = "StealthEye",
    executable = "eye",
    version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
    pid = Environment.ProcessId
}));

app.MapMcp("/mcp");

await app.RunAsync();
