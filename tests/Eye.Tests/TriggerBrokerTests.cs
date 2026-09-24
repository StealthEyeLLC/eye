using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Text.Json;
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
        var store = CreateStore();
        await using var broker = new TriggerBroker(store);
        await broker.InitializeAsync();

        using var process = Process.Start(new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "-NoProfile", "-Command", "Start-Sleep -Milliseconds 500" }
        })!;
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

    [Fact]
    public async Task FileExistsTrigger_UsesNativeWatcherAndAdvancesCursor()
    {
        Directory.CreateDirectory(_root);
        var target = Path.Combine(_root, "watched", "ready.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var store = CreateStore();
        await using var broker = new TriggerBroker(store);
        await broker.InitializeAsync();
        var created = broker.CreateFileExists(target, 5_000);

        await Task.Delay(100);
        await File.WriteAllTextAsync(target, "ready");

        var waited = await broker.WaitAsync(created.TriggerId, 5_000);
        Assert.False(waited.WaitTimedOut);
        Assert.Equal(TriggerStates.Satisfied, waited.Trigger.State);
        Assert.Equal(Path.GetFullPath(target), waited.Trigger.FilePath, ignoreCase: true);
        Assert.Equal(created.TriggerId, waited.Trigger.TriggerId);
        Assert.Equal(1, waited.Trigger.Incarnation);

        var read = broker.Read(created.TriggerId, 0, 10);
        var item = Assert.Single(read.Events);
        Assert.Equal("file_exists", item.EventType);
        using (var payload = JsonDocument.Parse(item.PayloadJson))
            Assert.Equal(Path.GetFullPath(target), payload.RootElement.GetProperty("file_path").GetString(), ignoreCase: true);
        Assert.Equal(1, read.NextCursor);
        Assert.True(read.Eof);
    }

    [Fact]
    public async Task PendingFileExistsTrigger_ReattachesAfterBrokerRestart()
    {
        Directory.CreateDirectory(_root);
        var target = Path.Combine(_root, "restart", "ready.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var store = CreateStore();
        string triggerId;

        await using (var first = new TriggerBroker(store))
        {
            await first.InitializeAsync();
            triggerId = first.CreateFileExists(target, 10_000).TriggerId;
        }

        await using var second = new TriggerBroker(store);
        await second.InitializeAsync();
        await Task.Delay(100);
        await File.WriteAllTextAsync(target, "ready");

        var waited = await second.WaitAsync(triggerId, 5_000);
        Assert.False(waited.WaitTimedOut);
        Assert.Equal(TriggerStates.Satisfied, waited.Trigger.State);
        Assert.Equal(triggerId, waited.Trigger.TriggerId);
        Assert.Equal(1, waited.Trigger.Incarnation);
        Assert.Equal(Path.GetFullPath(target), waited.Trigger.FilePath, ignoreCase: true);
    }
    [Fact]
    public void ExistingTriggerTable_IsMigratedForUiaRegistration()
    {
        var jobs = new JobStore(Path.Combine(_root, "legacy-state"), Path.Combine(_root, "legacy-spool"));
        using (var connection = new SqliteConnection($"Data Source={jobs.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE triggers (
                    trigger_id TEXT PRIMARY KEY,
                    incarnation INTEGER NOT NULL,
                    kind TEXT NOT NULL,
                    state TEXT NOT NULL,
                    created_utc TEXT NOT NULL,
                    completed_utc TEXT NULL,
                    deadline_utc TEXT NULL,
                    process_id INTEGER NULL,
                    process_start_utc TEXT NULL,
                    due_utc TEXT NULL,
                    file_path TEXT NULL,
                    failure_message TEXT NULL,
                    next_sequence INTEGER NOT NULL DEFAULT 1
                );
                """;
            command.ExecuteNonQuery();
        }

        var store = new TriggerStore(jobs);
        var created = store.CreateUiaChange("{\"windowId\":\"window_test\"}", null);
        var read = store.GetRequired(created.TriggerId);
        Assert.Equal(TriggerKinds.UiaChange, read.Kind);
        Assert.Equal(created.RegistrationJson, read.RegistrationJson);
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
