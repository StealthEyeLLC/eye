using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class SessionWorkerJobTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eye-session-worker-job-tests-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("user")]
    [InlineData("wsl")]
    public async Task DurableTerminalJob_UsesOnDemandSessionWorker(string context)
    {
        var store = new JobStore(Path.Combine(_root, "state"), Path.Combine(_root, "spool", "jobs"));
        var contract = EyeContractCatalog.Load();
        var manager = new JobManager(
            store,
            new ProcessRunner(),
            new SessionWorkerManager(WorkerExecutable(), contract.WorkerProtocolVersion));

        var job = manager.Start(new RunRequest
        {
            Context = context,
            FileName = context == "wsl" ? "sh" : "cmd.exe",
            WorkingDirectory = context == "wsl" ? "/mnt/x/repos/eye" : null,
            TimeoutMs = 15_000
        }, terminal: true, columns: 100, rows: 30);

        var resized = manager.Resize(job.JobId, 120, 40);
        Assert.True(resized.Terminal);
        Assert.Equal(120, resized.Columns);
        Assert.Equal(40, resized.Rows);

        var marker = "durable-session-worker-" + context + "-ok";
        if (context == "wsl")
            await manager.WriteAsync(job.JobId, $"printf '{marker}:%s:%s\\n' \"$(id -u)\" \"$PWD\"\rexit\r");
        else
            await manager.WriteAsync(job.JobId, $"echo {marker}\rexit\r");

        var waited = await manager.WaitAsync(job.JobId, 20_000);
        Assert.False(waited.WaitTimedOut);
        Assert.Equal(JobStates.Completed, waited.Job.State);
        Assert.Equal(context, waited.Job.Context);
        Assert.Contains("StealthEye", waited.Job.EffectiveIdentity, StringComparison.OrdinalIgnoreCase);

        var read = await manager.ReadAsync(job.JobId, "stdout", 0, 65_536);
        var compact = read.Text.Replace("\r", "").Replace("\n", "");
        Assert.Contains(marker, compact, StringComparison.OrdinalIgnoreCase);
        if (context == "wsl")
            Assert.Contains(marker + ":0:/mnt/x/repos/eye", compact, StringComparison.Ordinal);
        Assert.True(read.Eof);
    }

    private static string WorkerExecutable()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Eye.slnx")))
            directory = directory.Parent;
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
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
