using System.Text.Json;
using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

[Collection("Interactive desktop")]
public sealed class BrowserControlServiceTests : IDisposable
{
    private readonly string _stateRoot = Path.Combine(Path.GetTempPath(), "eye-browser-control-state-" + Guid.NewGuid().ToString("N"));
    private readonly string _spoolRoot = Path.Combine(Path.GetTempPath(), "eye-browser-control-spool-" + Guid.NewGuid().ToString("N"));
    private string? _profile;

    [Fact]
    public async Task StableTarget_NavigatesAndEvaluatesThroughRawCdp()
    {
        _profile = Path.Combine(
            @"C:\Users\StealthEye\AppData\Local\StealthEye\Eye\ChromeTests",
            Guid.NewGuid().ToString("N"));
        var workers = new SessionWorkerManager(WorkerExecutable(), WorkerRpcMethods.CurrentProtocolVersion);
        await using var sessions = new BrowserSessionManager(workers);
        await sessions.EnsureAsync(
            userDataDir: _profile,
            initialUrl: "data:text/html,<title>Before</title><p>before</p>");
        var jobs = new JobStore(_stateRoot, _spoolRoot);
        var targetStore = new BrowserTargetStore(jobs);
        var observe = new BrowserObservationService(sessions, targetStore);
        var control = new BrowserControlService(sessions, targetStore);

        BrowserTargetSnapshot first = null!;
        for (var i = 0; i < 30; i++)
        {
            first = await observe.ObserveAsync();
            if (first.Targets.Any(x => x.Type == "page" && x.Url.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase)))
                break;
            await Task.Delay(100);
        }
        var page = Assert.Single(first.Targets, x => x.Type == "page");

        var destination = "data:text/html,<title>EyeNavigated</title><main id='content'>hello-eye</main>";
        var navigation = await control.NavigateAsync(page.TargetId, destination);
        Assert.Equal(page.TargetId, navigation.TargetId);
        Assert.Equal(page.Incarnation, navigation.Incarnation);
        Assert.Null(navigation.ErrorText);

        BrowserEvaluateSnapshot title = null!;
        for (var i = 0; i < 30; i++)
        {
            title = await control.EvaluateAsync(page.TargetId, "document.title");
            if (title.Value is { } value && value.ValueKind == System.Text.Json.JsonValueKind.String && value.GetString() == "EyeNavigated")
                break;
            await Task.Delay(100);
        }
        Assert.False(title.Threw, title.ExceptionText);
        Assert.True(title.Value is { ValueKind: JsonValueKind.String }, JsonSerializer.Serialize(title));
        Assert.Equal("EyeNavigated", title.Value!.Value.GetString());

        var body = await control.EvaluateAsync(page.TargetId, "document.querySelector('#content').textContent");
        Assert.False(body.Threw, body.ExceptionText);
        Assert.Equal("hello-eye", body.Value!.Value.GetString());

        var failure = await control.EvaluateAsync(page.TargetId, "throw new Error('eye-boom')");
        Assert.True(failure.Threw);
        Assert.Contains("eye-boom", failure.ExceptionText, StringComparison.OrdinalIgnoreCase);

        BrowserTargetSnapshot second = null!;
        for (var i = 0; i < 30; i++)
        {
            second = await observe.ObserveAsync();
            var current = second.Targets.SingleOrDefault(x => x.TargetId == page.TargetId);
            if (current is not null && current.Title.Contains("EyeNavigated", StringComparison.OrdinalIgnoreCase))
                break;
            await Task.Delay(100);
        }
        var again = Assert.Single(second.Targets, x => x.TargetId == page.TargetId);
        Assert.Equal(page.Incarnation, again.Incarnation);
        Assert.Contains("EyeNavigated", again.Title, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("data:text/html", again.Url, StringComparison.OrdinalIgnoreCase);
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