using StealthEye.Contract;

namespace StealthEye.Runtime;

public sealed class DesktopObservationService(SessionWorkerManager workers, DesktopWindowStore windows)
{
    public async Task<DesktopWindowSnapshot> ObserveAsync(
        bool includeInvisible = false,
        CancellationToken cancellationToken = default)
    {
        var observation = await workers.ObserveWindowsAsync(includeInvisible, cancellationToken);
        return windows.Apply(observation);
    }
}