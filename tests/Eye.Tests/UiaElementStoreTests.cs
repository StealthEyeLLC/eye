using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class UiaElementStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eye-uia-store-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SameElement_PreservesIdentityAcrossMutablePropertyChanges()
    {
        var store = Store();
        var window = Window();
        var first = store.Apply(window, Observation(false, Element("1", name: "Before")));
        var second = store.Apply(window, Observation(false, Element("1", name: "After")));

        Assert.Equal(first.Elements[0].ElementId, second.Elements[0].ElementId);
        Assert.Equal(1, second.Elements[0].Incarnation);
        Assert.Equal(first.Cursor + 1, second.Cursor);
        Assert.Equal("After", second.Elements[0].Name);
    }

    [Fact]
    public void StructuralFingerprintChange_IncrementsIncarnation()
    {
        var store = Store();
        var window = Window();
        var first = store.Apply(window, Observation(false, Element("1", automationId: "a")));
        var second = store.Apply(window, Observation(false, Element("1", automationId: "b")));

        Assert.Equal(first.Elements[0].ElementId, second.Elements[0].ElementId);
        Assert.Equal(2, second.Elements[0].Incarnation);
    }

    [Fact]
    public void CompleteDisappearanceThenReturn_IncrementsIncarnation()
    {
        var store = Store();
        var window = Window();
        var first = store.Apply(window, Observation(false, Element("1")));
        store.Apply(window, Observation(false));
        var returned = store.Apply(window, Observation(false, Element("1")));

        Assert.Equal(first.Elements[0].ElementId, returned.Elements[0].ElementId);
        Assert.Equal(2, returned.Elements[0].Incarnation);
    }

    [Fact]
    public void TruncatedQuery_DoesNotDeactivateUnseenElements()
    {
        var store = Store();
        var window = Window();
        var first = store.Apply(window, Observation(false,
            Element("1"),
            Element("2", parent: "1", depth: 1)));
        store.Apply(window, Observation(true, Element("1")));
        var third = store.Apply(window, Observation(false,
            Element("1"),
            Element("2", parent: "1", depth: 1)));

        var firstChild = first.Elements.Single(x => x.Depth == 1);
        var thirdChild = third.Elements.Single(x => x.Depth == 1);
        Assert.Equal(firstChild.ElementId, thirdChild.ElementId);
        Assert.Equal(1, thirdChild.Incarnation);
        Assert.Equal(third.Elements[0].ElementId, thirdChild.ParentElementId);
    }

    [Fact]
    public void WindowIncarnationChange_IncrementsElementIncarnation()
    {
        var store = Store();
        var first = store.Apply(Window(1), Observation(false, Element("1")));
        var second = store.Apply(Window(2), Observation(false, Element("1")));

        Assert.Equal(first.Elements[0].ElementId, second.Elements[0].ElementId);
        Assert.Equal(2, second.Elements[0].Incarnation);
        Assert.Equal(2, second.WindowIncarnation);
    }

    [Fact]
    public void Reopen_PreservesIdentityAndCursor()
    {
        var jobs = Jobs();
        var firstStore = new UiaElementStore(jobs);
        var first = firstStore.Apply(Window(), Observation(false, Element("1")));
        var reopened = new UiaElementStore(new JobStore(jobs.StateRoot, jobs.SpoolRoot));
        var second = reopened.Apply(Window(), Observation(false, Element("1")));

        Assert.Equal(first.Elements[0].ElementId, second.Elements[0].ElementId);
        Assert.Equal(first.Elements[0].Incarnation, second.Elements[0].Incarnation);
        Assert.Equal(first.Cursor + 1, second.Cursor);
    }

    private UiaElementStore Store() => new(Jobs());
    private JobStore Jobs() => new(Path.Combine(_root, "state"), Path.Combine(_root, "spool", "jobs"));

    private static DesktopWindowTarget Window(long incarnation = 1) => new("window_test", incarnation, 1, 100);

    private static WorkerUiaQueryResult Observation(bool truncated, params WorkerUiaElementInfo[] elements) =>
        new(DateTimeOffset.UtcNow, truncated, elements);

    private static WorkerUiaElementInfo Element(
        string runtimeId,
        string? parent = null,
        int depth = 0,
        string name = "Element",
        string automationId = "automation",
        string controlType = "ControlType.Button",
        string frameworkId = "Win32",
        string className = "Button") => new(
            runtimeId,
            parent,
            depth,
            name,
            automationId,
            controlType,
            frameworkId,
            className,
            true,
            false,
            false,
            new WorkerWindowRect(1, 2, 3, 4));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}