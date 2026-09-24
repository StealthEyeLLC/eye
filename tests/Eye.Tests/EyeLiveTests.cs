using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using StealthEye.Contract;
using StealthEye.Runtime;
using StealthEye.Tools;

namespace Eye.Tests;

public sealed class EyeLiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eye-live-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void McpMetadataAndResource_AreCanonical()
    {
        var tool = EyeLiveMcp.CreateTool(EyeContractCatalog.Load());
        Assert.Equal("eye_live", tool.ProtocolTool.Name);
        Assert.Equal(EyeLiveMcp.ResourceUri, tool.ProtocolTool.Meta!["ui"]!["resourceUri"]!.GetValue<string>());
        Assert.NotNull(tool.ProtocolTool.OutputSchema);

        var method = typeof(EyeLiveResource).GetMethod(nameof(EyeLiveResource.Read), BindingFlags.Public | BindingFlags.Static)!;
        var resource = method.GetCustomAttribute<McpServerResourceAttribute>()!;
        Assert.Equal(EyeLiveMcp.ResourceUri, resource.UriTemplate);
        Assert.Equal(EyeLiveMcp.ResourceMimeType, resource.MimeType);
        Assert.Contains("ui/initialize", EyeLiveResource.Read(), StringComparison.Ordinal);
        Assert.Contains("tools/call", EyeLiveResource.Read(), StringComparison.Ordinal);
        Assert.DoesNotContain("http://", EyeLiveResource.Read(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", EyeLiveResource.Read(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Snapshot_RemainsUsefulWithEngineUnavailable_AndHidesStorageInternals()
    {
        var state = Path.Combine(_root, "state");
        var spool = Path.Combine(_root, "spool", "jobs");
        var engines = Path.Combine(_root, "engines");
        Directory.CreateDirectory(state);
        await File.WriteAllTextAsync(Path.Combine(state, "engine-state.json"), "{\"ActiveVersion\":\"A\",\"PreviousVersion\":null}");

        var jobs = new JobStore(state, spool);
        var paths = jobs.AllocatePaths("job_live_test");
        jobs.Create("job_live_test", new RunRequest { FileName = "cmd.exe" }, paths);
        jobs.Finish("job_live_test", JobStates.Completed, new ProcessRunResult(1, 0, false, "", "", "system", "test", 0));

        var triggers = new TriggerStore(jobs);
        triggers.CreateTime(DateTimeOffset.UtcNow.AddMinutes(1));

        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "artifact.txt");
        await File.WriteAllTextAsync(source, "eye-live");
        var artifacts = new ArtifactStore(jobs);
        var artifact = await artifacts.ImportFileAsync(source, "text", "text/plain", "artifact.txt", "eye-live-test");

        await using var engine = new EngineSupervisor(state, engines);
        var engineStatus = await engine.InitializeAsync();
        Assert.Equal("unavailable", engineStatus.State);
        var snapshot = new EyeLiveSnapshotService(jobs, triggers, artifacts, engine).Snapshot();

        Assert.Equal("unavailable", snapshot.Engine.State);
        Assert.Equal("A", snapshot.Engine.ActiveVersion);
        Assert.Contains(snapshot.Jobs, x => x.JobId == "job_live_test" && x.State == JobStates.Completed);
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
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}