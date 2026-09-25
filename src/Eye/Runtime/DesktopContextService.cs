using StealthEye.Contract;

namespace StealthEye.Runtime;

public sealed class DesktopContextService(SessionWorkerManager workers)
{
    public async Task<WorkerDesktopContextResult> ObserveAsync(
        CancellationToken cancellationToken = default)
    {
        return await workers.ObserveDesktopContextAsync(cancellationToken);
    }
}
