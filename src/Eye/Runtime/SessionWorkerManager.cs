using StealthEye.Contract;
namespace StealthEye.Runtime;

public sealed class SessionWorkerManager
{
    private readonly Func<string> _resolveWorkerExecutable;

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
        await using var worker = await StartAsync(cancellationToken);
        return await worker.ObserveWindowsAsync(includeInvisible, cancellationToken);
    }
    public async Task<WorkerUiaQueryResult> QueryUiaAsync(
        long hwnd,
        int maxDepth = 4,
        int maxNodes = 200,
        CancellationToken cancellationToken = default)
    {
        await using var worker = await StartAsync(cancellationToken);
        return await worker.QueryUiaAsync(hwnd, maxDepth, maxNodes, cancellationToken);
    }
    public async Task<WorkerUiaActionResult> ActUiaAsync(
        long hwnd,
        string runtimeId,
        string action,
        string? value = null,
        CancellationToken cancellationToken = default)
    {
        await using var worker = await StartAsync(cancellationToken);
        return await worker.ActUiaAsync(hwnd, runtimeId, action, value, cancellationToken);
    }
    public async Task<WorkerUiaChangeResult> WaitUiaChangeAsync(
        long hwnd,
        string? runtimeId,
        string[] eventTypes,
        int maxNodes = 5000,
        Action? onArmed = null,
        CancellationToken cancellationToken = default)
    {
        await using var worker = await StartAsync(cancellationToken);
        var armed = await worker.ArmUiaChangeAsync(hwnd, runtimeId, eventTypes, maxNodes, cancellationToken);
        if (!armed.Armed)
            throw new InvalidOperationException("UIA worker did not arm its change watcher.");
        onArmed?.Invoke();
        return await worker.WaitUiaChangeAsync(cancellationToken);
    }

    public Task<SessionWorker> StartAsync(CancellationToken cancellationToken = default) =>
        SessionWorker.StartAsync(
            ResolveWorkerExecutablePath(),
            WorkerProtocolVersion,
            TimeSpan.FromSeconds(10),
            cancellationToken);
}