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
    public Task<SessionWorker> StartAsync(CancellationToken cancellationToken = default) =>
        SessionWorker.StartAsync(
            ResolveWorkerExecutablePath(),
            WorkerProtocolVersion,
            TimeSpan.FromSeconds(10),
            cancellationToken);
}