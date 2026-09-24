using System.Text.Json;
using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

[Collection("Interactive desktop")]
public sealed class BrowserTriggerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "eye-browser-trigger-" + Guid.NewGuid().ToString("N"));
    private string? _profile;

    [Fact]
    public async Task NavigationTrigger_UsesCdpEventAndFeedsDurableHostQueue()
    {
        _profile = Path.Combine(
            @"C:\Users\StealthEye\AppData\Local\StealthEye\Eye\ChromeTests",
            Guid.NewGuid().ToString("N"));

        var contract = EyeContractCatalog.Load();
        await using var workers = new SessionWorkerManager(
            WorkerExecutable(),
            contract.WorkerProtocolVersion);
        await using var sessions = new BrowserSessionManager(workers);
        await sessions.EnsureAsync(
            userDataDir: _profile,
            initialUrl: "data:text/html,<title>TriggerBefore</title><main>before</main>");

        var jobs = new JobStore(
            Path.Combine(_root, "state"),
            Path.Combine(_root, "spool"));
        var targetStore = new BrowserTargetStore(jobs);
        var observation = new BrowserObservationService(sessions, targetStore);
        var control = new BrowserControlService(sessions, targetStore);
        var source = new BrowserTriggerSource(sessions, targetStore);
        var triggerStore = new TriggerStore(jobs);
        await using var broker = new TriggerBroker(triggerStore, null, source);
        await broker.InitializeAsync();

        BrowserTargetSnapshot snapshot = null!;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            snapshot = await observation.ObserveAsync();
            if (snapshot.Targets.Any(x =>
                    x.Type == "page" &&
                    x.Url.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase)))
                break;
            await Task.Delay(100);
        }

        var page = Assert.Single(snapshot.Targets, x => x.Type == "page");
        var created = broker.CreateBrowserNavigation(page.TargetId, 10_000);
        Assert.Equal(TriggerKinds.BrowserNavigation, created.Kind);
        await broker.WaitUntilArmedAsync(created.TriggerId);

        var destination =
            "data:text/html,<title>TriggerAfter</title><main id='done'>browser-trigger-ok</main>";
        var navigated = await control.NavigateAsync(page.TargetId, destination);
        Assert.Null(navigated.ErrorText);

        var waited = await broker.WaitAsync(created.TriggerId, 10_000);
        Assert.False(waited.WaitTimedOut);
        Assert.Equal(TriggerStates.Satisfied, waited.Trigger.State);

        var read = broker.Read(created.TriggerId, 0, 10);
        var item = Assert.Single(read.Events);
        Assert.Equal("browser_navigated", item.EventType);
        Assert.Equal(1, item.Sequence);
        Assert.Equal(1, read.NextCursor);
        Assert.True(read.Eof);

        using var payload = JsonDocument.Parse(item.PayloadJson);
        var root = payload.RootElement;
        Assert.Equal(page.TargetId, root.GetProperty("target_id").GetString());
        Assert.Equal(page.Incarnation, root.GetProperty("target_incarnation").GetInt64());
        Assert.StartsWith(
            "data:text/html",
            root.GetProperty("url").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string WorkerExecutable()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Eye.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(
            directory!.FullName,
            "src",
            "Eye.Worker",
            "bin",
            "Release",
            "net10.0-windows",
            "eye-worker.exe");
    }

    public void Dispose()
    {
        if (_profile is not null)
        {
            for (var attempt = 0; attempt < 40 && Directory.Exists(_profile); attempt++)
            {
                try { Directory.Delete(_profile, recursive: true); }
                catch (IOException) when (attempt < 39) { Thread.Sleep(100); }
                catch (UnauthorizedAccessException) when (attempt < 39) { Thread.Sleep(100); }
            }
        }

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
