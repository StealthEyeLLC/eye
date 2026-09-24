using System.Text.Json;
using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class MissionContinuationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eye-continuation-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ConsecutiveHandoffs_UseContextArtifactsAndRelayCursors()
    {
        var jobs = new JobStore(Path.Combine(_root, "state"), Path.Combine(_root, "spool", "jobs"));
        var artifacts = new ArtifactStore(jobs);
        var workers = new SessionWorkerManager(WorkerExecutable(), WorkerRpcMethods.CurrentProtocolVersion);
        var windows = new DesktopWindowStore(jobs);
        var desktop = new DesktopObservationService(workers, windows);
        var uia = new UiaQueryService(windows, workers, new UiaElementStore(jobs));
        var captures = new DesktopCaptureService(jobs, windows, workers, artifacts);
        await using var browserSessions = new BrowserSessionManager(workers);
        var browser = new BrowserObservationService(browserSessions, new BrowserTargetStore(jobs));
        var missions = new MissionBlackboardStore(jobs);
        var relay = new RelayService(missions);
        var context = new ContextCaptureService(jobs, missions, desktop, uia, captures, browser, artifacts);
        var continuation = new MissionContinuationService(context, relay);
        var mission = missions.Create("Move work between tabs");

        var first = await continuation.CaptureAndRelayAsync(mission.MissionId, "tab-a", "first slice complete");
        Assert.Equal(1, first.Context.MissionRevision);
        Assert.Equal(1, first.Relay.Cursor);
        Assert.Contains(first.Context.ArtifactId, first.Relay.Message, StringComparison.Ordinal);
        Assert.Contains("mission_revision=1", first.Relay.Message, StringComparison.Ordinal);
        Assert.Contains("first slice complete", first.Relay.Message, StringComparison.Ordinal);

        var firstRead = relay.Read(mission.MissionId, afterCursor: 0);
        Assert.Single(firstRead.Messages);
        Assert.Equal(first.Relay.RelayId, firstRead.Messages[0].RelayId);
        Assert.Equal(1, firstRead.NextCursor);

        var second = await continuation.CaptureAndRelayAsync(mission.MissionId, "tab-b", "second slice continuing");
        Assert.Equal(2, second.Context.MissionRevision);
        Assert.Equal(2, second.Relay.Cursor);
        Assert.NotEqual(first.Context.ArtifactId, second.Context.ArtifactId);

        var delta = relay.Read(mission.MissionId, afterCursor: firstRead.NextCursor);
        Assert.Single(delta.Messages);
        Assert.Equal(second.Relay.RelayId, delta.Messages[0].RelayId);
        Assert.Equal(2, delta.NextCursor);
        Assert.False(delta.Gap);

        var blackboard = missions.GetRequired(mission.MissionId);
        Assert.Equal(3, blackboard.Revision);
        Assert.Equal(1, blackboard.Incarnation);

        var firstArtifact = artifacts.Info(first.Context.ArtifactId);
        using var firstDoc = JsonDocument.Parse(await File.ReadAllTextAsync(firstArtifact.ContentPath));
        Assert.Equal(1, firstDoc.RootElement.GetProperty("mission").GetProperty("revision").GetInt64());
        Assert.Equal(JsonValueKind.Null, firstDoc.RootElement.GetProperty("browser").ValueKind);
        Assert.Null(browserSessions.Status);
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