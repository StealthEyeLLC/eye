using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class BrowserObservationServiceTests : IDisposable
{
    private readonly string _stateRoot = Path.Combine(Path.GetTempPath(), "eye-browser-observe-state-" + Guid.NewGuid().ToString("N"));
    private readonly string _spoolRoot = Path.Combine(Path.GetTempPath(), "eye-browser-observe-spool-" + Guid.NewGuid().ToString("N"));
    private string? _profile;

    [Fact]
    public async Task Service_MapsLiveChromeTargetsToStableHostIdentity()
    {
        _profile = Path.Combine(
            @"C:\Users\StealthEye\AppData\Local\StealthEye\Eye\ChromeTests",
            Guid.NewGuid().ToString("N"));
        var workers = new SessionWorkerManager(WorkerExecutable(), WorkerRpcMethods.CurrentProtocolVersion);
        await using var sessions = new BrowserSessionManager(workers);
        await sessions.EnsureAsync(
            userDataDir: _profile,
            initialUrl: "data:text/html,<title>EyeStableTarget</title><h1>stable</h1>");
        var jobs = new JobStore(_stateRoot, _spoolRoot);
        var service = new BrowserObservationService(sessions, new BrowserTargetStore(jobs));

        BrowserTargetSnapshot first = null!;
        for (var i = 0; i < 30; i++)
        {
            first = await service.ObserveAsync();
            if (first.Targets.Any(x => x.Type == "page" && x.Url.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase)))
                break;
            await Task.Delay(100);
        }
        var page = Assert.Single(first.Targets, x => x.Type == "page");
        Assert.StartsWith("target_", page.TargetId, StringComparison.Ordinal);
        Assert.Equal(1, page.Incarnation);
        Assert.Contains("EyeStableTarget", page.Title, StringComparison.OrdinalIgnoreCase);

        var second = await service.ObserveAsync();
        var again = Assert.Single(second.Targets, x => x.Type == "page");
        Assert.Equal(page.TargetId, again.TargetId);
        Assert.Equal(page.Incarnation, again.Incarnation);
        Assert.Equal(first.Cursor + 1, second.Cursor);
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
        if (_profile is not null)
        {
            for (var attempt = 0; attempt < 30 && Directory.Exists(_profile); attempt++)
            {
                try { Directory.Delete(_profile, recursive: true); }
                catch (IOException) when (attempt < 29) { Thread.Sleep(100); }
                catch (UnauthorizedAccessException) when (attempt < 29) { Thread.Sleep(100); }
            }
        }
        if (Directory.Exists(_stateRoot)) Directory.Delete(_stateRoot, recursive: true);
        if (Directory.Exists(_spoolRoot)) Directory.Delete(_spoolRoot, recursive: true);
    }
}