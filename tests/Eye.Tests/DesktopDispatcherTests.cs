using System.Text.Json;
using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class DesktopDispatcherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eye-desktop-dispatch-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task UiObserve_UsesInspectFacadeAndReturnsStablePublicWindows()
    {
        var contract = EyeContractCatalog.Load();
        var jobs = new JobStore(Path.Combine(_root, "state"), Path.Combine(_root, "spool", "jobs"));
        var workers = new SessionWorkerManager(WorkerExecutable(), contract.WorkerProtocolVersion);
        var windowStore = new DesktopWindowStore(jobs);
        var desktop = new DesktopObservationService(workers, windowStore);
        var uia = new UiaQueryService(windowStore, workers, new UiaElementStore(jobs));
        var dispatcher = new EyeDispatcher(
            new JobManager(jobs, new ProcessRunner()),
            new ArtifactStore(jobs),
            desktopObservationService: desktop,
            uiaQueryService: uia);

        var result = JsonSerializer.SerializeToElement(await dispatcher.ExecuteAsync(
            EyeEffectClass.Inspect,
            "ui.observe",
            null));

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal("ui.observe", result.GetProperty("operation").GetString());
        var payload = result.GetProperty("result");
        Assert.True(payload.GetProperty("cursor").GetInt64() >= 1);
        Assert.True(payload.GetProperty("session_id").GetInt32() > 0);
        var windows = payload.GetProperty("windows");
        Assert.True(windows.GetArrayLength() > 0);
        Assert.All(windows.EnumerateArray(), window =>
        {
            Assert.StartsWith("window_", window.GetProperty("window_id").GetString(), StringComparison.Ordinal);
            Assert.True(window.GetProperty("incarnation").GetInt64() >= 1);
            Assert.False(window.TryGetProperty("hwnd", out _));
            Assert.False(window.TryGetProperty("thread_id", out _));
            Assert.True(window.GetProperty("bounds").TryGetProperty("left", out _));
        });
        Assert.Contains(windows.EnumerateArray(), window =>
            window.TryGetProperty("uia", out var uia) &&
            !string.IsNullOrWhiteSpace(uia.GetProperty("control_type").GetString()));

        var target = windows.EnumerateArray().First(window =>
            window.GetProperty("foreground").GetBoolean() && window.TryGetProperty("uia", out _));
        var queried = JsonSerializer.SerializeToElement(await dispatcher.ExecuteAsync(
            EyeEffectClass.Inspect,
            "ui.query",
            JsonSerializer.SerializeToElement(new UiQueryArgs(target.GetProperty("window_id").GetString()!, 2, 80))));
        Assert.True(queried.GetProperty("ok").GetBoolean(), queried.ToString());
        Assert.Equal("ui.query", queried.GetProperty("operation").GetString());
        var queryResult = queried.GetProperty("result");
        Assert.Equal(target.GetProperty("window_id").GetString(), queryResult.GetProperty("window_id").GetString());
        var elements = queryResult.GetProperty("elements");
        Assert.True(elements.GetArrayLength() > 0);
        Assert.All(elements.EnumerateArray(), element =>
        {
            Assert.StartsWith("element_", element.GetProperty("element_id").GetString(), StringComparison.Ordinal);
            Assert.False(element.TryGetProperty("runtime_id", out _));
        });
        var wrongFacade = JsonSerializer.SerializeToElement(await dispatcher.ExecuteAsync(
            EyeEffectClass.Interact,
            "ui.observe",
            JsonSerializer.SerializeToElement(new UiObserveArgs())));
        Assert.False(wrongFacade.GetProperty("ok").GetBoolean());
        Assert.Equal("wrong_tool", wrongFacade.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("eye_inspect", wrongFacade.GetProperty("error").GetProperty("expected").GetProperty("tool").GetString());
    }

    private static string WorkerExecutable()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Eye.slnx")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "src", "Eye.Worker", "bin", "Release", "net10.0-windows", "eye-worker.exe");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}