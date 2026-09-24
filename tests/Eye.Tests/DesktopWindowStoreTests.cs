using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class DesktopWindowStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eye-desktop-store-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SameWindow_PreservesIdentityAndIncarnation_WhileCursorAdvances()
    {
        var store = Store();
        var first = store.Apply(Observation(Window(title: "First", left: 10)));
        var second = store.Apply(Observation(Window(title: "Second", left: 20)));

        var a = Assert.Single(first.Windows);
        var b = Assert.Single(second.Windows);
        Assert.Equal(a.WindowId, b.WindowId);
        Assert.Equal(1, a.Incarnation);
        Assert.Equal(1, b.Incarnation);
        Assert.Equal(first.Cursor + 1, second.Cursor);
        Assert.Equal("Second", b.Title);
        Assert.Equal(20, b.Left);
    }

    [Fact]
    public void DisappearanceThenReappearance_IncrementsIncarnationButKeepsStableId()
    {
        var store = Store();
        var first = store.Apply(Observation(Window()));
        var missing = store.Apply(Observation());
        var returned = store.Apply(Observation(Window()));

        Assert.Empty(missing.Windows);
        Assert.Equal(first.Cursor + 1, missing.Cursor);
        Assert.Equal(missing.Cursor + 1, returned.Cursor);
        Assert.Equal(first.Windows[0].WindowId, returned.Windows[0].WindowId);
        Assert.Equal(2, returned.Windows[0].Incarnation);
    }

    [Fact]
    public void ReusedHwndWithDifferentProcessGeneration_IncrementsIncarnation()
    {
        var store = Store();
        var first = store.Apply(Observation(Window(processStart: new DateTimeOffset(2026, 8, 8, 1, 0, 0, TimeSpan.Zero))));
        var second = store.Apply(Observation(Window(processStart: new DateTimeOffset(2026, 8, 8, 1, 1, 0, TimeSpan.Zero))));

        Assert.Equal(first.Windows[0].WindowId, second.Windows[0].WindowId);
        Assert.Equal(2, second.Windows[0].Incarnation);
    }

    [Fact]
    public void StoreReopen_PreservesIdentityIncarnationAndCursor()
    {
        var jobs = Jobs();
        var firstStore = new DesktopWindowStore(jobs);
        var first = firstStore.Apply(Observation(Window()));

        var reopened = new DesktopWindowStore(new JobStore(jobs.StateRoot, jobs.SpoolRoot));
        var second = reopened.Apply(Observation(Window()));

        Assert.Equal(first.Windows[0].WindowId, second.Windows[0].WindowId);
        Assert.Equal(first.Windows[0].Incarnation, second.Windows[0].Incarnation);
        Assert.Equal(first.Cursor + 1, second.Cursor);
    }

    private DesktopWindowStore Store() => new(Jobs());

    private JobStore Jobs() => new(
        Path.Combine(_root, "state"),
        Path.Combine(_root, "spool", "jobs"));

    private static WorkerDesktopObservationResult Observation(params WorkerWindowInfo[] windows) => new(
        1,
        DateTimeOffset.UtcNow,
        windows);

    private static WorkerWindowInfo Window(
        long hwnd = 100,
        int processId = 200,
        DateTimeOffset? processStart = null,
        string processName = "app",
        int threadId = 300,
        string title = "Window",
        string className = "TestWindowClass",
        bool visible = true,
        bool minimized = false,
        bool foreground = true,
        int left = 10) => new(
            hwnd,
            processId,
            processStart ?? new DateTimeOffset(2026, 8, 8, 1, 0, 0, TimeSpan.Zero),
            processName,
            threadId,
            title,
            className,
            visible,
            minimized,
            foreground,
            new WorkerWindowRect(left, 20, left + 800, 620));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}