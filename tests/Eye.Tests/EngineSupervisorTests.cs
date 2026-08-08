using System.Diagnostics;
using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class EngineSupervisorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eye-engine-supervisor-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Supervisor_ActivatesRestartsRollsBackAndPersistsSelection()
    {
        var state = Path.Combine(_root, "state");
        var engines = Path.Combine(_root, "engines");
        Stage(engines, "A");
        Stage(engines, "B");

        int restartedPid;
        await using (var supervisor = new EngineSupervisor(state, engines))
        {
            Assert.Equal("not_configured", supervisor.Status().State);

            var a = await supervisor.ActivateAsync("A");
            Assert.Equal("healthy", a.State);
            Assert.Equal("A", a.ActiveVersion);
            Assert.Null(a.PreviousVersion);
            var aPid = a.ProcessId!.Value;

            var b = await supervisor.ActivateAsync("B");
            Assert.Equal("B", b.ActiveVersion);
            Assert.Equal("A", b.PreviousVersion);
            Assert.True(ProcessGone(aPid));
            var bPid = b.ProcessId!.Value;

            await Assert.ThrowsAsync<FileNotFoundException>(() => supervisor.ActivateAsync("missing"));
            var afterFailure = supervisor.Status();
            Assert.Equal("healthy", afterFailure.State);
            Assert.Equal("B", afterFailure.ActiveVersion);
            Assert.Equal(bPid, afterFailure.ProcessId);

            var restarted = await supervisor.RestartAsync();
            Assert.Equal("B", restarted.ActiveVersion);
            Assert.Equal("A", restarted.PreviousVersion);
            Assert.NotEqual(bPid, restarted.ProcessId);
            Assert.True(ProcessGone(bPid));
            restartedPid = restarted.ProcessId!.Value;

            var rolledBack = await supervisor.RollbackAsync();
            Assert.Equal("A", rolledBack.ActiveVersion);
            Assert.Equal("B", rolledBack.PreviousVersion);
            Assert.True(ProcessGone(restartedPid));
        }

        await using var reopened = new EngineSupervisor(state, engines);
        var initialized = await reopened.InitializeAsync();
        Assert.Equal("healthy", initialized.State);
        Assert.Equal("A", initialized.ActiveVersion);
        Assert.Equal("B", initialized.PreviousVersion);
    }

    [Fact]
    public async Task Initialize_WithMissingSelectedEngine_LeavesHostUnavailableNotCrashed()
    {
        var state = Path.Combine(_root, "state-missing");
        var engines = Path.Combine(_root, "engines-missing");
        Directory.CreateDirectory(state);
        Directory.CreateDirectory(engines);
        await File.WriteAllTextAsync(Path.Combine(state, "engine-state.json"), "{\"ActiveVersion\":\"missing\",\"PreviousVersion\":null}");

        await using var supervisor = new EngineSupervisor(state, engines);
        var status = await supervisor.InitializeAsync();
        Assert.Equal("unavailable", status.State);
        Assert.Equal("missing", status.ActiveVersion);
        Assert.Null(status.ProcessId);
        Assert.NotNull(status.LastError);
    }

    private static bool ProcessGone(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static void Stage(string engineRoot, string version)
    {
        var source = EngineOutputDirectory();
        var destination = Path.Combine(engineRoot, version);
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "eye-engine.*"))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
    }

    private static string EngineOutputDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Eye.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var path = Path.Combine(directory!.FullName, "src", "Eye.Engine", "bin", "Release", "net10.0-windows");
        Assert.True(File.Exists(Path.Combine(path, "eye-engine.exe")));
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}