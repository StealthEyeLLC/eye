using System.Diagnostics;
using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class TriggerBrokerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eye-trigger-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TimeTrigger_SatisfiesAndAdvancesEventCursor()
    {
        var store = CreateStore();
        await using var broker = new TriggerBroker(store);
        await broker.InitializeAsync();
        var created = broker.CreateTime(DateTimeOffset.UtcNow.AddMilliseconds(100));

        var waited = await broker.WaitAsync(created.TriggerId, 5_000);
        Assert.False(waited.WaitTimedOut);
        Assert.Equal(TriggerStates.Satisfied, waited.Trigger.State);
        Assert.Equal(created.TriggerId, waited.Trigger.TriggerId);
        Assert.Equal(1, waited.Trigger.Incarnation);

        var read = broker.Read(created.TriggerId, 0, 10);
        var item = Assert.Single(read.Events);
        Assert.Equal(1, item.Sequence);
        Assert.Equal("time_reached", item.EventType);
        Assert.Equal(1, read.NextCursor);
        Assert.True(read.Eof);
    }

    [Fact]
    public async Task ProcessExitTrigger_UsesProcessIncarnationAndNativeExitWait()
    {
        using var process = Process.Start(new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "-NoProfile", "-Command", "Start-Sleep -Milliseconds 250" }
        })!;
        var store = CreateStore();
        await using var broker = new TriggerBroker(store);
        await broker.InitializeAsync();
        var created = broker.CreateProcessExit(process.Id, 5_000);

        var waited = await broker.WaitAsync(created.TriggerId, 5_000);
        Assert.False(waited.WaitTimedOut);
        Assert.Equal(TriggerStates.Satisfied, waited.Trigger.State);
        Assert.Equal(process.Id, waited.Trigger.ProcessId);
        Assert.NotNull(waited.Trigger.ProcessStartAt);
        Assert.Equal("process_exited", Assert.Single(broker.Read(created.TriggerId, 0, 10).Events).EventType);
    }

    [Fact]
    public async Task PendingTimeTrigger_ReattachesAfterBrokerRestart()
    {
        var store = CreateStore();
        string triggerId;
        await using (var first = new TriggerBroker(store))
        {
            await first.InitializeAsync();
            triggerId = first.CreateTime(DateTimeOffset.UtcNow.AddMilliseconds(400)).TriggerId;
        }

        await using var second = new TriggerBroker(store);
        await second.InitializeAsync();
        var waited = await second.WaitAsync(triggerId, 5_000);
        Assert.Equal(TriggerStates.Satisfied, waited.Trigger.State);
        Assert.Equal(triggerId, waited.Trigger.TriggerId);
        Assert.Equal(1, waited.Trigger.Incarnation);
    }

    [Fact]
    public async Task CancelledTrigger_IsDurableAndReadable()
    {
        var store = CreateStore();
        await using var broker = new TriggerBroker(store);
        await broker.InitializeAsync();
        var created = broker.CreateTime(DateTimeOffset.UtcNow.AddMinutes(1));
        var cancelled = broker.Cancel(created.TriggerId);

        Assert.Equal(TriggerStates.Cancelled, cancelled.State);
        var read = broker.Read(created.TriggerId, 0, 10);
        Assert.Equal("trigger_cancelled", Assert.Single(read.Events).EventType);
        Assert.True(read.Eof);
        Assert.Equal(TriggerStates.Cancelled, broker.Status(created.TriggerId).State);
    }

    private TriggerStore CreateStore()
    {
        var jobs = new JobStore(Path.Combine(_root, "state"), Path.Combine(_root, "spool", "jobs"));
        return new TriggerStore(jobs);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
