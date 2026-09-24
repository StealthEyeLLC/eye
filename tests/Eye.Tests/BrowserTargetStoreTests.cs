using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class BrowserTargetStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eye-browser-targets-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SameTarget_PreservesIdentityWhileMutableMetadataChanges()
    {
        var store = Store();
        var first = store.Apply(Observation(Target("raw-1", "page", "Before", "https://example.test/one")));
        var second = store.Apply(Observation(Target("raw-1", "page", "After", "https://example.test/two")));

        Assert.Equal(first.Targets[0].TargetId, second.Targets[0].TargetId);
        Assert.Equal(1, second.Targets[0].Incarnation);
        Assert.Equal(first.Cursor + 1, second.Cursor);
        Assert.Equal("After", second.Targets[0].Title);
        Assert.Equal("https://example.test/two", second.Targets[0].Url);
    }

    [Fact]
    public void DisappearanceThenReturn_IncrementsIncarnation()
    {
        var store = Store();
        var first = store.Apply(Observation(Target("raw-1")));
        store.Apply(Observation());
        var returned = store.Apply(Observation(Target("raw-1")));

        Assert.Equal(first.Targets[0].TargetId, returned.Targets[0].TargetId);
        Assert.Equal(2, returned.Targets[0].Incarnation);
    }

    [Fact]
    public void TargetTypeChange_IncrementsIncarnation()
    {
        var store = Store();
        var first = store.Apply(Observation(Target("raw-1", "page")));
        var second = store.Apply(Observation(Target("raw-1", "service_worker")));

        Assert.Equal(first.Targets[0].TargetId, second.Targets[0].TargetId);
        Assert.Equal(2, second.Targets[0].Incarnation);
    }

    [Fact]
    public void ResolveActive_ReturnsPrivateCdpHandle_AndReopenPreservesCursor()
    {
        var jobs = Jobs();
        var firstStore = new BrowserTargetStore(jobs);
        var first = firstStore.Apply(Observation(Target("raw-1", "page", "Title", "about:blank")));
        var stableId = first.Targets[0].TargetId;
        var handle = firstStore.ResolveActive(stableId);
        Assert.Equal("raw-1", handle.CdpTargetId);
        Assert.Equal(stableId, handle.TargetId);

        var reopened = new BrowserTargetStore(new JobStore(jobs.StateRoot, jobs.SpoolRoot));
        var second = reopened.Apply(Observation(Target("raw-1", "page", "Again", "about:blank")));
        Assert.Equal(stableId, second.Targets[0].TargetId);
        Assert.Equal(first.Cursor + 1, second.Cursor);
    }

    private BrowserTargetStore Store() => new(Jobs());
    private JobStore Jobs() => new(Path.Combine(_root, "state"), Path.Combine(_root, "spool", "jobs"));
    private static WorkerBrowserTargetsResult Observation(params WorkerBrowserTargetInfo[] targets) => new(
        DateTimeOffset.UtcNow,
        "Chrome/Test",
        "1.3",
        targets);
    private static WorkerBrowserTargetInfo Target(
        string cdpTargetId,
        string type = "page",
        string title = "Title",
        string url = "about:blank") => new(cdpTargetId, type, title, url);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}