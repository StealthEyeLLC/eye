using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using StealthEye.Contract;
using StealthEye.Runtime;
using StealthEye.Tools;

namespace Eye.Tests;

public sealed class EyeLiveTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "eye-live-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void McpMetadataResourceAndAppOnlyHelpers_AreCanonical()
    {
        var tool = EyeLiveMcp.CreateTool(EyeContractCatalog.Load());
        Assert.Equal("eye_live", tool.ProtocolTool.Name);
        Assert.Equal(
            EyeLiveMcp.ResourceUri,
            tool.ProtocolTool.Meta!["ui"]!["resourceUri"]!.GetValue<string>());
        Assert.NotNull(tool.ProtocolTool.OutputSchema);

        var appTools = EyeLiveMcp.CreateAppTools();
        Assert.Equal(
            ["eye_live_action", "eye_live_refresh"],
            appTools.Select(x => x.ProtocolTool.Name).Order(StringComparer.Ordinal).ToArray());
        foreach (var appTool in appTools)
        {
            var meta = appTool.ProtocolTool.Meta!.ToJsonString()
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal);
            Assert.Contains("\"visibility\":[\"app\"]", meta, StringComparison.Ordinal);
        }

        var method = typeof(EyeLiveResource).GetMethod(
            nameof(EyeLiveResource.Read),
            BindingFlags.Public | BindingFlags.Static)!;
        var resource = method.GetCustomAttribute<McpServerResourceAttribute>()!;
        Assert.Equal(EyeLiveMcp.ResourceUri, resource.UriTemplate);
        Assert.Equal(EyeLiveMcp.ResourceMimeType, resource.MimeType);

        var html = EyeLiveResource.Read();
        Assert.Contains("ui/initialize", html, StringComparison.Ordinal);
        Assert.Contains("tools/call", html, StringComparison.Ordinal);
        Assert.Contains("eye_live_refresh", html, StringComparison.Ordinal);
        Assert.Contains("eye_live_action", html, StringComparison.Ordinal);
        Assert.Contains("ui/message", html, StringComparison.Ordinal);
        Assert.Contains("id=\"missions\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"relay\"", html, StringComparison.Ordinal);
        Assert.Contains("stdout_tail", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Snapshot_RemainsUsefulWithEngineUnavailable_AndHidesStorageInternals()
    {
        var state = Path.Combine(_root, "state");
        var spool = Path.Combine(_root, "spool", "jobs");
        var engines = Path.Combine(_root, "engines");
        Directory.CreateDirectory(state);
        await File.WriteAllTextAsync(
            Path.Combine(state, "engine-state.json"),
            "{\"ActiveVersion\":\"A\",\"PreviousVersion\":null}");

        var jobs = new JobStore(state, spool);
        var paths = jobs.AllocatePaths("job_live_test");
        jobs.Create(
            "job_live_test",
            new RunRequest { FileName = "cmd.exe" },
            paths);
        await File.WriteAllTextAsync(paths.Stdout, "eye-live-stdout-tail");
        await File.WriteAllTextAsync(paths.Stderr, "eye-live-stderr-tail");
        jobs.Finish(
            "job_live_test",
            JobStates.Completed,
            new ProcessRunResult(1, 0, false, "", "", "system", "test", 0));

        var triggers = new TriggerStore(jobs);
        triggers.CreateTime(DateTimeOffset.UtcNow.AddMinutes(1));

        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "artifact.txt");
        await File.WriteAllTextAsync(source, "eye-live");
        var artifacts = new ArtifactStore(jobs);
        var artifact = await artifacts.ImportFileAsync(
            source,
            "text",
            "text/plain",
            "artifact.txt",
            "eye-live-test");

        var blackboard = new MissionBlackboardStore(jobs);
        var mission = blackboard.Create("Prove Eye Live remains useful without the engine.");
        mission = blackboard.Update(
            mission.MissionId,
            new MissionBlackboardUpdate(NextAction: "Inspect the compact supervision state."));
        var relayEntry = new RelayService(blackboard).Send(
            mission.MissionId,
            "operator",
            "Eye Live relay is available.");

        await using var engine = new EngineSupervisor(state, engines);
        var engineStatus = await engine.InitializeAsync();
        Assert.Equal("unavailable", engineStatus.State);

        var snapshot = new EyeLiveSnapshotService(
            jobs,
            triggers,
            artifacts,
            blackboard,
            engine).Snapshot();

        Assert.Equal("unavailable", snapshot.Engine.State);
        Assert.Equal("A", snapshot.Engine.ActiveVersion);
        Assert.Equal(1, snapshot.Context.MissionCount);
        Assert.Equal(0, snapshot.Context.ActiveJobCount);
        Assert.Equal(1, snapshot.Context.PendingTriggerCount);
        Assert.Equal(1, snapshot.Context.RelayMessageCount);
        Assert.Equal(mission.MissionId, snapshot.Context.LatestMissionId);

        var liveJob = Assert.Single(snapshot.Jobs, x => x.JobId == "job_live_test");
        Assert.Equal(JobStates.Completed, liveJob.State);
        Assert.Contains("eye-live-stdout-tail", liveJob.StdoutTail, StringComparison.Ordinal);
        Assert.Contains("eye-live-stderr-tail", liveJob.StderrTail, StringComparison.Ordinal);

        var liveMission = Assert.Single(snapshot.Missions);
        Assert.Equal(mission.MissionId, liveMission.MissionId);
        Assert.Equal(mission.Objective, liveMission.Objective);
        Assert.Equal(mission.NextAction, liveMission.NextAction);

        var liveRelay = Assert.Single(snapshot.Relay);
        Assert.Equal(relayEntry.Cursor, liveRelay.Cursor);
        Assert.Equal(relayEntry.Message, liveRelay.Message);

        Assert.Single(snapshot.Triggers);
        Assert.Contains(snapshot.Artifacts, x => x.ArtifactId == artifact.ArtifactId);

        var json = JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain("stdout_path", json, StringComparison.Ordinal);
        Assert.DoesNotContain("stderr_path", json, StringComparison.Ordinal);
        Assert.DoesNotContain("content_path", json, StringComparison.Ordinal);
        Assert.DoesNotContain(spool, json, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}