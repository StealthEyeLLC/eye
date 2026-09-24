using StealthEye.Contract;

namespace StealthEye.Runtime;

public sealed class SessionWorkerManager : IAsyncDisposable
{
    private static readonly TimeSpan IdleLifetime = TimeSpan.FromSeconds(15);

    private readonly Func<string> _resolveWorkerExecutable;
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private SessionWorker? _cachedWorker;
    private string? _cachedWorkerExecutablePath;
    private int _activeLeases;
    private CancellationTokenSource? _idleCancellation;
    private Task _idleTask = Task.CompletedTask;
    private int _disposed;

    public SessionWorkerManager(string workerExecutablePath, string workerProtocolVersion)
    {
        var fixedPath = Path.GetFullPath(workerExecutablePath);
        _resolveWorkerExecutable = () => fixedPath;
        WorkerProtocolVersion = workerProtocolVersion;
    }

    public SessionWorkerManager(EngineSupervisor engineSupervisor, string workerProtocolVersion)
    {
        _resolveWorkerExecutable = engineSupervisor.ResolveActiveWorkerExecutable;
        WorkerProtocolVersion = workerProtocolVersion;
    }

    public string WorkerProtocolVersion { get; }
    public string ResolveWorkerExecutablePath() => Path.GetFullPath(_resolveWorkerExecutable());

    public async Task<WorkerDesktopObservationResult> ObserveWindowsAsync(
        bool includeInvisible = false,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await LeaseAsync(cancellationToken);
        return await lease.Worker.ObserveWindowsAsync(includeInvisible, cancellationToken);
    }

    public async Task<WorkerUiaQueryResult> QueryUiaAsync(
        long hwnd,
        int maxDepth = 4,
        int maxNodes = 200,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await LeaseAsync(cancellationToken);
        return await lease.Worker.QueryUiaAsync(hwnd, maxDepth, maxNodes, cancellationToken);
    }

    public async Task<WorkerUiaActionResult> ActUiaAsync(
        long hwnd,
        string runtimeId,
        string action,
        string? value = null,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await LeaseAsync(cancellationToken);
        return await lease.Worker.ActUiaAsync(hwnd, runtimeId, action, value, cancellationToken);
    }

    public async Task<WorkerUiaChangeResult> WaitUiaChangeAsync(
        long hwnd,
        string? runtimeId,
        string[] eventTypes,
        int maxNodes = 5000,
        Action? onArmed = null,
        CancellationToken cancellationToken = default)
    {
        // A long-running watch owns a dedicated ephemeral worker so ordinary
        // UIA actions remain independently dispatchable while the watch waits.
        await using var worker = await StartAsync(cancellationToken);
        var armed = await worker.ArmUiaChangeAsync(
            hwnd,
            runtimeId,
            eventTypes,
            maxNodes,
            cancellationToken);
        if (!armed.Armed)
            throw new InvalidOperationException("UIA worker did not arm its change watcher.");
        onArmed?.Invoke();
        return await worker.WaitUiaChangeAsync(cancellationToken);
    }

    public async Task<WorkerWindowCaptureResult> CaptureWindowAsync(
        long hwnd,
        string destinationPath,
        int timeoutMs = 5000,
        bool recognizeText = false,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await LeaseAsync(cancellationToken);
        return await lease.Worker.CaptureWindowAsync(
            hwnd,
            destinationPath,
            timeoutMs,
            recognizeText,
            cancellationToken);
    }

    // Explicit callers (browser session ownership, low-level tests) still receive
    // an independent worker and own its lifetime.
    public Task<SessionWorker> StartAsync(CancellationToken cancellationToken = default) =>
        SessionWorker.StartAsync(
            ResolveWorkerExecutablePath(),
            WorkerProtocolVersion,
            TimeSpan.FromSeconds(10),
            cancellationToken);

    private async Task<WorkerLease> LeaseAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _cacheGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            CancelIdleShutdown();

            var desiredPath = ResolveWorkerExecutablePath();
            if (_cachedWorker is null ||
                _cachedWorker.HasExited ||
                !string.Equals(
                    _cachedWorkerExecutablePath,
                    desiredPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_cachedWorker is not null)
                {
                    try { await _cachedWorker.DisposeAsync(); } catch { }
                }

                _cachedWorker = await SessionWorker.StartAsync(
                    desiredPath,
                    WorkerProtocolVersion,
                    TimeSpan.FromSeconds(10),
                    cancellationToken);
                _cachedWorkerExecutablePath = desiredPath;
            }

            _activeLeases++;
            return new WorkerLease(this, _cachedWorker);
        }
        finally
        {
            _cacheGate.Release();
        }
    }

    private async ValueTask ReleaseAsync()
    {
        await _cacheGate.WaitAsync();
        try
        {
            if (_activeLeases <= 0)
                return;

            _activeLeases--;
            if (_activeLeases == 0 && Volatile.Read(ref _disposed) == 0)
                ScheduleIdleShutdown();
        }
        finally
        {
            _cacheGate.Release();
        }
    }

    private void ScheduleIdleShutdown()
    {
        CancelIdleShutdown();
        var cancellation = new CancellationTokenSource();
        _idleCancellation = cancellation;
        _idleTask = RunIdleShutdownAsync(cancellation);
    }

    private async Task RunIdleShutdownAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(IdleLifetime, cancellation.Token);
            await _cacheGate.WaitAsync(cancellation.Token);
            try
            {
                if (_activeLeases != 0 ||
                    !ReferenceEquals(_idleCancellation, cancellation))
                    return;

                var worker = _cachedWorker;
                _cachedWorker = null;
                _cachedWorkerExecutablePath = null;
                _idleCancellation = null;
                if (worker is not null)
                    await worker.DisposeAsync();
            }
            finally
            {
                _cacheGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private void CancelIdleShutdown()
    {
        var cancellation = _idleCancellation;
        _idleCancellation = null;
        if (cancellation is null)
            return;
        try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        CancelIdleShutdown();
        try { await _idleTask; } catch { }

        await _cacheGate.WaitAsync();
        try
        {
            if (_cachedWorker is not null)
                await _cachedWorker.DisposeAsync();
            _cachedWorker = null;
            _cachedWorkerExecutablePath = null;
            _activeLeases = 0;
        }
        finally
        {
            _cacheGate.Release();
            _cacheGate.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(SessionWorkerManager));
    }

    private sealed class WorkerLease(
        SessionWorkerManager owner,
        SessionWorker worker) : IAsyncDisposable
    {
        private int _released;

        internal SessionWorker Worker { get; } = worker;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
                return;
            await owner.ReleaseAsync();
        }
    }
}
