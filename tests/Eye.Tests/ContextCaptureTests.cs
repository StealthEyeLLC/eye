using System.Text.Json;
using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

[Collection("Interactive desktop")]
public sealed class ContextCaptureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eye-context-" + Guid.NewGuid().ToString("N"));
    private string? _profile;

    [Fact]
    public async Task Capture_ProducesStableIdOnlyArtifactWithoutStartingBrowser()
    {
        var services = CreateServices();
        await using var browserSessions = services.BrowserSessions;
        var mission = services.Missions.Create("Continue Phase 7 across tabs");
        services.Missions.Update(mission.MissionId, new MissionBlackboardUpdate(
            Facts: ["Context capture is one-shot."],
            Decisions: ["Do not launch Chrome just to capture context."],
            NextAction: "Inspect the context artifact."));
        services.Relay.Send(mission.MissionId, "tab-a", "Blackboard and Relay are ready.");

        Assert.Null(browserSessions.Status);
        var result = await services.Context.CaptureAsync(mission.MissionId);
        Assert.Null(browserSessions.Status);

        Assert.Equal(mission.MissionId, result.MissionId);
        Assert.True(result.MissionRevision >= 3);
        Assert.StartsWith("artifact_", result.ArtifactId, StringComparison.Ordinal);
        Assert.Null(result.ScreenshotArtifactId);

        var artifact = services.Artifacts.Info(result.ArtifactId);
        Assert.Equal("context", artifact.Kind);
        Assert.Equal("application/json", artifact.MimeType);
        Assert.Equal("hot", artifact.StorageTier);
        Assert.Equal(result.SizeBytes, artifact.SizeBytes);
        Assert.Equal(result.Sha256, artifact.Sha256);

        var json = await File.ReadAllTextAsync(artifact.ContentPath);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(mission.MissionId, root.GetProperty("mission").GetProperty("mission_id").GetString());
        Assert.Equal(result.MissionRevision, root.GetProperty("mission").GetProperty("revision").GetInt64());
        Assert.True(root.GetProperty("desktop").GetProperty("windows").GetArrayLength() > 0);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("browser").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("selected_paths").ValueKind);
        Assert.True(root.TryGetProperty("clipboard_text", out _));
        Assert.True(root.TryGetProperty("selection_text", out _));
        Assert.True(root.TryGetProperty("foreground_process_path", out _));
        Assert.True(root.TryGetProperty("explorer_path", out _));
        Assert.Contains("Blackboard and Relay are ready.", json, StringComparison.Ordinal);

        Assert.DoesNotContain("hwnd", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("runtime_id", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cdp_target_id", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("content_path", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stdout_path", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stderr_path", json, StringComparison.OrdinalIgnoreCase);

        var contextRoot = Path.Combine(Directory.GetParent(services.Jobs.SpoolRoot)!.FullName, "context");
        Assert.Empty(Directory.EnumerateFiles(contextRoot, "*.json"));
    }

    [Fact]
    public async Task Capture_IncludesAlreadyActiveBrowserWithoutChangingTargetIdentity()
    {
        var services = CreateServices();
        await using var browserSessions = services.BrowserSessions;
        _profile = Path.Combine(
            @"C:\Users\StealthEye\AppData\Local\StealthEye\Eye\ChromeTests",
            Guid.NewGuid().ToString("N"));
        await browserSessions.EnsureAsync(
            userDataDir: _profile,
            initialUrl: "data:text/html,<title>EyeContextBrowser</title><main>context</main>");
        Assert.NotNull(browserSessions.Status);

        BrowserTargetSnapshot observed = null!;
        for (var i = 0; i < 30; i++)
        {
            observed = await services.BrowserObservation.ObserveAsync();
            if (observed.Targets.Any(x => x.Type == "page" && x.Title.Contains("EyeContextBrowser", StringComparison.OrdinalIgnoreCase)))
                break;
            await Task.Delay(100);
        }
        var page = Assert.Single(observed.Targets, x => x.Type == "page");
        var mission = services.Missions.Create("Capture active browser context");

        var result = await services.Context.CaptureAsync(mission.MissionId);
        Assert.NotNull(browserSessions.Status);
        var artifact = services.Artifacts.Info(result.ArtifactId);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(artifact.ContentPath));
        var browser = document.RootElement.GetProperty("browser");
        Assert.NotEqual(JsonValueKind.Null, browser.ValueKind);
        var targets = browser.GetProperty("targets");
        var capturedPage = Assert.Single(targets.EnumerateArray(), x => x.GetProperty("type").GetString() == "page");
        Assert.Equal(page.TargetId, capturedPage.GetProperty("target_id").GetString());
        Assert.Equal(page.Incarnation, capturedPage.GetProperty("incarnation").GetInt64());
        Assert.False(capturedPage.TryGetProperty("cdp_target_id", out _));
    }

    private Services CreateServices()
    {
        var state = Path.Combine(_root, Guid.NewGuid().ToString("N"), "state");
        var spool = Path.Combine(_root, Guid.NewGuid().ToString("N"), "spool", "jobs");
        var jobs = new JobStore(state, spool);
        var artifacts = new ArtifactStore(jobs);
        var workers = new SessionWorkerManager(WorkerExecutable(), WorkerRpcMethods.CurrentProtocolVersion);
        var windows = new DesktopWindowStore(jobs);
        var desktop = new DesktopObservationService(workers, windows);
        var uia = new UiaQueryService(windows, workers, new UiaElementStore(jobs));
        var captures = new DesktopCaptureService(jobs, windows, workers, artifacts);
        var browserSessions = new BrowserSessionManager(workers);
        var browserObservation = new BrowserObservationService(browserSessions, new BrowserTargetStore(jobs));
        var missions = new MissionBlackboardStore(jobs);
        var relay = new RelayService(missions);
        var context = new ContextCaptureService(jobs, missions, desktop, uia, captures, browserObservation, artifacts, new DesktopContextService(workers));
        return new Services(jobs, artifacts, browserSessions, browserObservation, missions, relay, context);
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
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed record Services(
        JobStore Jobs,
        ArtifactStore Artifacts,
        BrowserSessionManager BrowserSessions,
        BrowserObservationService BrowserObservation,
        MissionBlackboardStore Missions,
        RelayService Relay,
        ContextCaptureService Context);
}