using System.Diagnostics;
using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

[Collection("Interactive desktop")]
public sealed class SessionWorkerLifecycleTests
{
    [Fact]
    public async Task CachedWorkerCrash_IsReplacedWithoutRestartingHost()
    {
        var hostPid = Environment.ProcessId;
        var contract = EyeContractCatalog.Load();
        await using var manager = new SessionWorkerManager(
            WorkerExecutable(),
            contract.WorkerProtocolVersion,
            TimeSpan.FromSeconds(30));

        _ = await manager.ObserveWindowsAsync();
        var firstPid = AssertWorkerPid(manager.CachedWorkerProcessId);

        using (var first = Process.GetProcessById(firstPid))
        {
            first.Kill(entireProcessTree: true);
            await first.WaitForExitAsync();
        }

        _ = await manager.ObserveWindowsAsync();
        var secondPid = AssertWorkerPid(manager.CachedWorkerProcessId);

        Assert.NotEqual(firstPid, secondPid);
        Assert.Equal(hostPid, Environment.ProcessId);
        Assert.False(Process.GetProcessById(secondPid).HasExited);
    }

    [Fact]
    public async Task CachedWorker_IsDestroyedAfterIdleLeaseWindow()
    {
        var contract = EyeContractCatalog.Load();
        await using var manager = new SessionWorkerManager(
            WorkerExecutable(),
            contract.WorkerProtocolVersion,
            TimeSpan.FromMilliseconds(250));

        _ = await manager.ObserveWindowsAsync();
        var workerPid = AssertWorkerPid(manager.CachedWorkerProcessId);

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < deadline &&
               manager.CachedWorkerProcessId is not null)
            await Task.Delay(50);

        Assert.Null(manager.CachedWorkerProcessId);
        Assert.True(ProcessGone(workerPid));
    }

    private static int AssertWorkerPid(int? processId)
    {
        Assert.True(processId is > 0);
        return processId!.Value;
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

    private static string WorkerExecutable()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Eye.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(
            directory!.FullName,
            "src",
            "Eye.Worker",
            "bin",
            "Release",
            "net10.0-windows",
            "eye-worker.exe");
    }
}