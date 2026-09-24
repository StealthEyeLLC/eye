using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

[Collection("Interactive desktop")]
public sealed class DesktopObservationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eye-desktop-observe-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Service_MapsRealActiveDesktopToStableHostIdentity()
    {
        var contract = EyeContractCatalog.Load();
        var workers = new SessionWorkerManager(WorkerExecutable(), contract.WorkerProtocolVersion);
        var jobs = new JobStore(Path.Combine(_root, "state"), Path.Combine(_root, "spool", "jobs"));
        var service = new DesktopObservationService(workers, new DesktopWindowStore(jobs));

        var first = await service.ObserveAsync();
        var second = await service.ObserveAsync();

        Assert.True(first.SessionId > 0);
        Assert.Equal(first.SessionId, second.SessionId);
        Assert.NotEmpty(first.Windows);
        Assert.NotEmpty(second.Windows);
        Assert.Equal(first.Cursor + 1, second.Cursor);

        var firstByHwnd = first.Windows.ToDictionary(x => x.Hwnd);
        var stable = second.Windows.FirstOrDefault(x => firstByHwnd.ContainsKey(x.Hwnd));
        Assert.NotNull(stable);
        var prior = firstByHwnd[stable!.Hwnd];
        Assert.Equal(prior.WindowId, stable.WindowId);
        Assert.Equal(prior.Incarnation, stable.Incarnation);
        Assert.StartsWith("window_", stable.WindowId, StringComparison.Ordinal);
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
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}