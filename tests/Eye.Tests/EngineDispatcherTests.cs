using System.Text.Json;
using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class EngineDispatcherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eye-engine-dispatcher-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PublicEngineControls_StatusActivateRestartRollback()
    {
        var state = Path.Combine(_root, "state");
        var engines = Path.Combine(_root, "engines");
        Stage(engines, "A");
        Stage(engines, "B");

        var jobStore = new JobStore(Path.Combine(_root, "jobs-state"), Path.Combine(_root, "jobs"));
        await using var supervisor = new EngineSupervisor(state, engines);
        var dispatcher = new EyeDispatcher(new JobManager(jobStore, new ProcessRunner()), new ArtifactStore(jobStore), supervisor);

        var initial = Element(await dispatcher.ExecuteAsync(EyeEffectClass.Inspect, "engine.status", null));
        Assert.True(initial.GetProperty("ok").GetBoolean());
        Assert.Equal("not_configured", initial.GetProperty("result").GetProperty("state").GetString());

        var a = Element(await dispatcher.ExecuteAsync(
            EyeEffectClass.Change,
            "engine.activate",
            JsonSerializer.SerializeToElement(new EngineActivateArgs("A"))));
        Assert.Equal("healthy", a.GetProperty("result").GetProperty("state").GetString());
        Assert.Equal("A", a.GetProperty("result").GetProperty("active_version").GetString());
        var aPid = a.GetProperty("result").GetProperty("process_id").GetInt32();

        var b = Element(await dispatcher.ExecuteAsync(
            EyeEffectClass.Change,
            "engine.activate",
            JsonSerializer.SerializeToElement(new EngineActivateArgs("B"))));
        Assert.Equal("B", b.GetProperty("result").GetProperty("active_version").GetString());
        Assert.Equal("A", b.GetProperty("result").GetProperty("previous_version").GetString());

        var restarted = Element(await dispatcher.ExecuteAsync(EyeEffectClass.Change, "engine.restart", null));
        Assert.Equal("B", restarted.GetProperty("result").GetProperty("active_version").GetString());
        Assert.NotEqual(aPid, restarted.GetProperty("result").GetProperty("process_id").GetInt32());

        var rolledBack = Element(await dispatcher.ExecuteAsync(EyeEffectClass.Change, "engine.rollback", null));
        Assert.Equal("A", rolledBack.GetProperty("result").GetProperty("active_version").GetString());
        Assert.Equal("B", rolledBack.GetProperty("result").GetProperty("previous_version").GetString());

        var status = Element(await dispatcher.ExecuteAsync(EyeEffectClass.Inspect, "engine.status", null));
        Assert.Equal("healthy", status.GetProperty("result").GetProperty("state").GetString());
        Assert.Equal("A", status.GetProperty("result").GetProperty("active_version").GetString());
    }

    [Fact]
    public async Task FailedPublicActivation_LeavesCurrentEngineHealthy()
    {
        var state = Path.Combine(_root, "state-failure");
        var engines = Path.Combine(_root, "engines-failure");
        Stage(engines, "A");
        var jobStore = new JobStore(Path.Combine(_root, "jobs-state-failure"), Path.Combine(_root, "jobs-failure"));
        await using var supervisor = new EngineSupervisor(state, engines);
        var dispatcher = new EyeDispatcher(new JobManager(jobStore, new ProcessRunner()), new ArtifactStore(jobStore), supervisor);

        var active = Element(await dispatcher.ExecuteAsync(
            EyeEffectClass.Change, "engine.activate", JsonSerializer.SerializeToElement(new EngineActivateArgs("A"))));
        var pid = active.GetProperty("result").GetProperty("process_id").GetInt32();

        var failed = Element(await dispatcher.ExecuteAsync(
            EyeEffectClass.Change, "engine.activate", JsonSerializer.SerializeToElement(new EngineActivateArgs("missing"))));
        Assert.False(failed.GetProperty("ok").GetBoolean());
        Assert.Equal("operation_failed", failed.GetProperty("error").GetProperty("code").GetString());

        var status = Element(await dispatcher.ExecuteAsync(EyeEffectClass.Inspect, "engine.status", null));
        Assert.Equal("healthy", status.GetProperty("result").GetProperty("state").GetString());
        Assert.Equal(pid, status.GetProperty("result").GetProperty("process_id").GetInt32());
    }

    private static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value);

    private static void Stage(string engineRoot, string version)
    {
        var source = EngineOutputDirectory();
        var destination = Path.Combine(engineRoot, version);
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
    }

    private static string EngineOutputDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Eye.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "Eye.Engine", "bin", "Release", "net10.0-windows");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
