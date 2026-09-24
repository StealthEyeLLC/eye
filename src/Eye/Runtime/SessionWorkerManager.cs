namespace StealthEye.Runtime;

public sealed class SessionWorkerManager(string workerExecutablePath, string workerProtocolVersion)
{
    public string WorkerExecutablePath { get; } = Path.GetFullPath(workerExecutablePath);
    public string WorkerProtocolVersion { get; } = workerProtocolVersion;

    public Task<SessionWorker> StartAsync(CancellationToken cancellationToken = default) =>
        SessionWorker.StartAsync(
            WorkerExecutablePath,
            WorkerProtocolVersion,
            TimeSpan.FromSeconds(10),
            cancellationToken);
}
