using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace StealthEye.Runtime;

public sealed class TriggerBroker(TriggerStore store, UiaTriggerSource? uiaTriggerSource = null) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ActiveTrigger> _active = new(StringComparer.Ordinal);
    private int _initialized;
    private int _disposed;

    public Task InitializeAsync()
    {
        ThrowIfDisposed();
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return Task.CompletedTask;

        foreach (var trigger in store.GetPending())
            EnsureActive(trigger);
        return Task.CompletedTask;
    }

    public TriggerRecord CreateProcessExit(int processId, int timeoutMs = 0)
    {
        ThrowIfDisposed();
        if (processId <= 0)
            throw new ArgumentException("process_id must be positive.", nameof(processId));
        if (timeoutMs < 0 || timeoutMs > 86_400_000)
            throw new ArgumentException("timeout_ms must be between 0 and 86400000.", nameof(timeoutMs));

        var processStart = TryGetProcessStart(processId);
        DateTimeOffset? deadline = timeoutMs > 0 ? DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs) : null;
        var trigger = store.CreateProcessExit(processId, processStart, deadline);
        EnsureActive(trigger);
        return trigger;
    }

    public TriggerRecord CreateTime(DateTimeOffset dueAt)
    {
        ThrowIfDisposed();
        var trigger = store.CreateTime(dueAt);
        EnsureActive(trigger);
        return trigger;
    }

    public TriggerRecord CreateFileExists(string filePath, int timeoutMs = 0)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("file_path is required.", nameof(filePath));
        if (timeoutMs < 0 || timeoutMs > 86_400_000)
            throw new ArgumentException("timeout_ms must be between 0 and 86400000.", nameof(timeoutMs));

        var fullPath = Path.GetFullPath(filePath);
        DateTimeOffset? deadline = timeoutMs > 0 ? DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs) : null;
        var trigger = store.CreateFileExists(fullPath, deadline);
        EnsureActive(trigger);
        return trigger;
    }
    public TriggerRecord CreateUiaChange(
        string windowId,
        string? elementId = null,
        string[]? eventTypes = null,
        int timeoutMs = 0)
    {
        ThrowIfDisposed();
        if (timeoutMs < 0 || timeoutMs > 86_400_000)
            throw new ArgumentException("timeout_ms must be between 0 and 86400000.", nameof(timeoutMs));
        var source = uiaTriggerSource ?? throw new InvalidOperationException("UIA trigger source is not configured.");
        var registration = source.PrepareRegistration(windowId, elementId, eventTypes);
        DateTimeOffset? deadline = timeoutMs > 0 ? DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs) : null;
        var trigger = store.CreateUiaChange(registration, deadline);
        EnsureActive(trigger);
        return trigger;
    }
    public async Task WaitUntilArmedAsync(string triggerId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var trigger = store.GetRequired(triggerId);
        if (trigger.Kind != TriggerKinds.UiaChange)
            throw new ArgumentException("trigger_id is not a UIA change trigger.", nameof(triggerId));
        if (TriggerStates.IsTerminal(trigger.State))
            return;
        var active = EnsureActive(trigger);
        await active.Ready.Task.WaitAsync(cancellationToken);
    }
    public TriggerRecord Status(string triggerId) => store.GetRequired(triggerId);

    public async Task<TriggerWaitResult> WaitAsync(
        string triggerId,
        int waitMs,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (waitMs < 0 || waitMs > 86_400_000)
            throw new ArgumentException("wait_ms must be between 0 and 86400000.", nameof(waitMs));

        var current = store.GetRequired(triggerId);
        if (TriggerStates.IsTerminal(current.State))
            return new TriggerWaitResult(current, false);

        var active = EnsureActive(current);
        if (waitMs == 0)
            return new TriggerWaitResult(await active.Completion.Task.WaitAsync(cancellationToken), false);

        var delay = Task.Delay(waitMs, cancellationToken);
        var winner = await Task.WhenAny(active.Completion.Task, delay);
        if (winner == active.Completion.Task)
            return new TriggerWaitResult(await active.Completion.Task, false);
        await delay;
        return new TriggerWaitResult(store.GetRequired(triggerId), true);
    }

    public TriggerReadResult Read(string triggerId, long cursor, int maxEvents)
    {
        ThrowIfDisposed();
        var trigger = store.GetRequired(triggerId);
        var events = store.ReadEvents(triggerId, cursor, maxEvents);
        var nextCursor = events.Length == 0 ? cursor : events[^1].Sequence;
        var eof = TriggerStates.IsTerminal(trigger.State) && nextCursor >= store.GetLatestSequence(triggerId);
        return new TriggerReadResult(triggerId, cursor, events, nextCursor, eof);
    }

    public TriggerRecord Cancel(string triggerId)
    {
        ThrowIfDisposed();
        var current = store.GetRequired(triggerId);
        if (TriggerStates.IsTerminal(current.State))
            return current;

        _active.TryGetValue(triggerId, out var active);
        active?.Cancellation.Cancel();
        var completed = store.Complete(
            triggerId,
            TriggerStates.Cancelled,
            "trigger_cancelled",
            JsonSerializer.Serialize(new { reason = "cancelled" }));
        active?.Completion.TrySetResult(completed);
        return completed;
    }

    private ActiveTrigger EnsureActive(TriggerRecord trigger)
    {
        if (TriggerStates.IsTerminal(trigger.State))
            throw new InvalidOperationException($"Trigger {trigger.TriggerId} is already terminal.");
        var active = _active.GetOrAdd(trigger.TriggerId, _ => new ActiveTrigger(trigger.TriggerId));
        if (active.TryStart())
            active.WatchTask = WatchAsync(trigger, active);
        return active;
    }

    private async Task WatchAsync(TriggerRecord trigger, ActiveTrigger active)
    {
        try
        {
            switch (trigger.Kind)
            {
                case TriggerKinds.ProcessExit:
                    await WatchProcessExitAsync(trigger, active);
                    break;
                case TriggerKinds.Time:
                    await WatchTimeAsync(trigger, active);
                    break;
                case TriggerKinds.FileExists:
                    await WatchFileExistsAsync(trigger, active);
                    break;
                case TriggerKinds.UiaChange:
                    await WatchUiaChangeAsync(trigger, active);
                    break;
                default:
                    Complete(active, TriggerStates.Failed, "trigger_failed", new { kind = trigger.Kind }, $"Unsupported trigger kind: {trigger.Kind}");
                    break;
            }
        }
        catch (OperationCanceledException) when (active.Cancellation.IsCancellationRequested)
        {
            // Cancellation is persisted by Cancel(), or disposal leaves the trigger pending for restart.
        }
        catch (Exception ex)
        {
            Complete(active, TriggerStates.Failed, "trigger_failed", new { error = ex.Message }, ex.Message);
        }
        finally
        {
            _active.TryRemove(trigger.TriggerId, out _);
            active.Cancellation.Dispose();
        }
    }

    private async Task WatchProcessExitAsync(TriggerRecord trigger, ActiveTrigger active)
    {
        if (trigger.ProcessId is null)
            throw new InvalidOperationException("Process-exit trigger is missing process_id.");
        if (trigger.ProcessStartAt is null)
        {
            Complete(active, TriggerStates.Satisfied, "process_exited", new { process_id = trigger.ProcessId, reason = "already_exited" });
            return;
        }

        Process process;
        try
        {
            process = Process.GetProcessById(trigger.ProcessId.Value);
        }
        catch (ArgumentException)
        {
            Complete(active, TriggerStates.Satisfied, "process_exited", new { process_id = trigger.ProcessId, reason = "not_found" });
            return;
        }

        using (process)
        {
            DateTimeOffset actualStart;
            try
            {
                actualStart = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
            }
            catch (InvalidOperationException)
            {
                Complete(active, TriggerStates.Satisfied, "process_exited", new { process_id = trigger.ProcessId, reason = "not_found" });
                return;
            }

            if (Math.Abs((actualStart - trigger.ProcessStartAt.Value).TotalMilliseconds) > 1)
            {
                Complete(active, TriggerStates.Satisfied, "process_exited", new { process_id = trigger.ProcessId, reason = "pid_reused" });
                return;
            }

            var exitTask = process.WaitForExitAsync(active.Cancellation.Token);
            if (trigger.DeadlineAt is not null)
            {
                var remaining = trigger.DeadlineAt.Value - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    Complete(active, TriggerStates.TimedOut, "trigger_timed_out", new { process_id = trigger.ProcessId });
                    return;
                }

                var delay = Task.Delay(remaining, active.Cancellation.Token);
                if (await Task.WhenAny(exitTask, delay) == delay)
                {
                    await delay;
                    Complete(active, TriggerStates.TimedOut, "trigger_timed_out", new { process_id = trigger.ProcessId });
                    return;
                }
            }

            await exitTask;
            int? exitCode = null;
            try { exitCode = process.ExitCode; } catch { }
            Complete(active, TriggerStates.Satisfied, "process_exited", new { process_id = trigger.ProcessId, exit_code = exitCode });
        }
    }

    private async Task WatchTimeAsync(TriggerRecord trigger, ActiveTrigger active)
    {
        if (trigger.DueAt is null)
            throw new InvalidOperationException("Time trigger is missing due_at.");
        var remaining = trigger.DueAt.Value - DateTimeOffset.UtcNow;
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, active.Cancellation.Token);
        Complete(active, TriggerStates.Satisfied, "time_reached", new { due_at = trigger.DueAt });
    }

    private TriggerRecord Complete(
        ActiveTrigger active,
        string state,
        string eventType,
        object payload,
        string? failureMessage = null)
    {
        var completed = store.Complete(active.TriggerId, state, eventType, JsonSerializer.Serialize(payload), failureMessage);
        active.Completion.TrySetResult(completed);
        return completed;
    }

    private async Task WatchFileExistsAsync(TriggerRecord trigger, ActiveTrigger active)
    {
        var filePath = trigger.FilePath
            ?? throw new InvalidOperationException("File-exists trigger is missing file_path.");
        filePath = Path.GetFullPath(filePath);
        if (File.Exists(filePath))
        {
            Complete(active, TriggerStates.Satisfied, "file_exists", new { file_path = filePath, reason = "already_exists" });
            return;
        }

        var watchRoot = FindExistingWatchRoot(filePath);
        var signal = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(watchRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        void Check(string candidate)
        {
            if (string.Equals(Path.GetFullPath(candidate), filePath, StringComparison.OrdinalIgnoreCase) && File.Exists(filePath))
                signal.TrySetResult("created");
        }

        FileSystemEventHandler changed = (_, e) => Check(e.FullPath);
        RenamedEventHandler renamed = (_, e) => Check(e.FullPath);
        ErrorEventHandler error = (_, e) => signal.TrySetException(e.GetException());
        watcher.Created += changed;
        watcher.Changed += changed;
        watcher.Renamed += renamed;
        watcher.Error += error;
        watcher.EnableRaisingEvents = true;

        if (File.Exists(filePath))
            signal.TrySetResult("already_exists");

        string reason;
        if (trigger.DeadlineAt is null)
        {
            reason = await signal.Task.WaitAsync(active.Cancellation.Token);
        }
        else
        {
            var remaining = trigger.DeadlineAt.Value - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                Complete(active, TriggerStates.TimedOut, "trigger_timed_out", new { file_path = filePath });
                return;
            }

            var delay = Task.Delay(remaining, active.Cancellation.Token);
            var winner = await Task.WhenAny(signal.Task, delay);
            if (winner == delay)
            {
                await delay;
                Complete(active, TriggerStates.TimedOut, "trigger_timed_out", new { file_path = filePath });
                return;
            }
            reason = await signal.Task;
        }

        Complete(active, TriggerStates.Satisfied, "file_exists", new { file_path = filePath, reason });
    }

    private async Task WatchUiaChangeAsync(TriggerRecord trigger, ActiveTrigger active)
    {
        var source = uiaTriggerSource ?? throw new InvalidOperationException("UIA trigger source is not configured.");
        var waitTask = source.WaitAsync(trigger, () => active.Ready.TrySetResult(true), active.Cancellation.Token);
        UiaTriggerEvent change;
        if (trigger.DeadlineAt is null)
        {
            change = await waitTask;
        }
        else
        {
            var remaining = trigger.DeadlineAt.Value - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                Complete(active, TriggerStates.TimedOut, "trigger_timed_out", new { kind = trigger.Kind });
                return;
            }
            var delay = Task.Delay(remaining, active.Cancellation.Token);
            if (await Task.WhenAny(waitTask, delay) == delay)
            {
                await delay;
                Complete(active, TriggerStates.TimedOut, "trigger_timed_out", new { kind = trigger.Kind });
                return;
            }
            change = await waitTask;
        }

        Complete(active, TriggerStates.Satisfied, "uia_changed", new
        {
            occurred_at = change.OccurredAt,
            event_type = change.EventType,
            window_id = change.WindowId,
            window_incarnation = change.WindowIncarnation,
            element_id = change.ElementId,
            element_incarnation = change.ElementIncarnation,
            property = change.Property,
            value = change.Value
        });
    }
    private static string FindExistingWatchRoot(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath)
            ?? throw new ArgumentException("file_path must include a parent directory.", nameof(filePath));
        while (!Directory.Exists(directory))
        {
            directory = Path.GetDirectoryName(directory)
                ?? throw new DirectoryNotFoundException($"No existing ancestor directory for {filePath}.");
        }
        return directory;
    }
    private static DateTimeOffset? TryGetProcessStart(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        var active = _active.Values.ToArray();
        foreach (var item in active)
        {
            try { item.Cancellation.Cancel(); } catch (ObjectDisposedException) { }
        }
        await Task.WhenAll(active.Select(item => item.WatchTask));
        _active.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(TriggerBroker));
    }

    private sealed class ActiveTrigger(string triggerId)
    {
        private int _started;
        public string TriggerId { get; } = triggerId;
        public CancellationTokenSource Cancellation { get; } = new();
        public TaskCompletionSource<TriggerRecord> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task WatchTask { get; set; } = Task.CompletedTask;
        public bool TryStart() => Interlocked.Exchange(ref _started, 1) == 0;
    }
}
