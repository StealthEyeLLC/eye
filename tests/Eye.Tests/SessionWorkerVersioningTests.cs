using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class SessionWorkerVersioningTests : IDisposable
{
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "StealthEye", "tests", "eye-worker-versioning-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WorkerResolver_FollowsActiveEngineVersion()
    {
        var state = Path.Combine(_root, "state");
        var engines = Path.Combine(_root, "engines");
        Stage(engines, "A");
        Stage(engines, "B");

        await using var supervisor = new EngineSupervisor(state, engines);
        var manager = new SessionWorkerManager(supervisor, WorkerRpcMethods.CurrentProtocolVersion);
        Assert.Throws<InvalidOperationException>(() => manager.ResolveWorkerExecutablePath());

        await supervisor.ActivateAsync("A");
        var a = manager.ResolveWorkerExecutablePath();
        Assert.Equal(Path.Combine(engines, "A", "eye-worker.exe"), a, ignoreCase: true);
        Assert.True(File.Exists(a));

        await supervisor.ActivateAsync("B");
        var b = manager.ResolveWorkerExecutablePath();
        Assert.Equal(Path.Combine(engines, "B", "eye-worker.exe"), b, ignoreCase: true);
        Assert.True(File.Exists(b));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task SelectedVersionWorker_RemainsAvailableWhenEngineIsUnavailable()
    {
        var state = Path.Combine(_root, "degraded-state");
        var engines = Path.Combine(_root, "degraded-engines");
        var versionDirectory = Path.Combine(engines, "A");
        Directory.CreateDirectory(state);
        Directory.CreateDirectory(versionDirectory);
        foreach (var file in Directory.GetFiles(WorkerOutputDirectory()))
            File.Copy(file, Path.Combine(versionDirectory, Path.GetFileName(file)), overwrite: true);
        await File.WriteAllTextAsync(
            Path.Combine(state, "engine-state.json"),
            "{\"ActiveVersion\":\"A\",\"PreviousVersion\":null}");

        await using var supervisor = new EngineSupervisor(state, engines);
        var status = await supervisor.InitializeAsync();
        Assert.Equal("unavailable", status.State);
        Assert.Equal("A", status.ActiveVersion);

        var manager = new SessionWorkerManager(supervisor, WorkerRpcMethods.CurrentProtocolVersion);
        await using var worker = await manager.StartAsync();
        Assert.Contains(Path.Combine("A", "eye-worker.exe"), worker.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(WorkerRpcMethods.CurrentProtocolVersion, worker.Handshake.WorkerProtocolVersion);
    }
    private static void Stage(string engineRoot, string version)
    {
        var destination = Path.Combine(engineRoot, version);
        Directory.CreateDirectory(destination);
        foreach (var source in new[] { EngineOutputDirectory(), WorkerOutputDirectory() })
        {
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
    }

    private static string OutputDirectory(string project)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Eye.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", project, "bin", "Release", "net10.0-windows");
    }

    private static string EngineOutputDirectory() => OutputDirectory("Eye.Engine");
    private static string WorkerOutputDirectory() => OutputDirectory("Eye.Worker");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}