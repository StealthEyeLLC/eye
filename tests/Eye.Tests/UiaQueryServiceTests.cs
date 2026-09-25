using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

[Collection("Interactive desktop")]
public sealed class UiaQueryServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eye-uia-query-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Service_QueriesStableElementsByWindowId()
    {
        var contract = EyeContractCatalog.Load();
        var jobs = new JobStore(Path.Combine(_root, "state"), Path.Combine(_root, "spool", "jobs"));
        var workerManager = new SessionWorkerManager(WorkerExecutable(), contract.WorkerProtocolVersion);
        var windowStore = new DesktopWindowStore(jobs);
        var desktop = new DesktopObservationService(workerManager, windowStore);
        var elements = new UiaElementStore(jobs);
        var query = new UiaQueryService(windowStore, workerManager, elements);

        DesktopWindowState? target = null;
        for (var attempt = 0; attempt < 30 && target is null; attempt++)
        {
            var observed = await desktop.ObserveAsync();
            target = observed.Windows.FirstOrDefault(window => window.Foreground && window.Uia is not null)
                ?? observed.Windows.FirstOrDefault(window => window.Uia is not null);
            if (target is null)
                await Task.Delay(100);
        }

        Assert.NotNull(target);
        var first = await query.QueryAsync(target!.WindowId, maxDepth: 2, maxNodes: 80);
        var second = await query.QueryAsync(target.WindowId, maxDepth: 2, maxNodes: 80);

        Assert.Equal(target.WindowId, first.WindowId);
        Assert.Equal(target.Incarnation, first.WindowIncarnation);
        Assert.NotEmpty(first.Elements);
        Assert.True(first.Elements.Length <= 80);
        Assert.Equal(first.Cursor + 1, second.Cursor);
        Assert.StartsWith("element_", first.Elements[0].ElementId, StringComparison.Ordinal);
        Assert.Null(first.Elements[0].ParentElementId);
        Assert.Equal(first.Elements[0].ElementId, second.Elements[0].ElementId);
        Assert.Equal(first.Elements[0].Incarnation, second.Elements[0].Incarnation);
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
