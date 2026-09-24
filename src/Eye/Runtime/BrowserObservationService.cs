namespace StealthEye.Runtime;

public sealed class BrowserObservationService(BrowserSessionManager sessions, BrowserTargetStore targets)
{
    public async Task<BrowserTargetSnapshot> ObserveAsync(CancellationToken cancellationToken = default)
    {
        var observation = await sessions.ObserveTargetsAsync(cancellationToken);
        return targets.Apply(observation);
    }
    public async Task<BrowserTargetSnapshot?> TryObserveActiveAsync(CancellationToken cancellationToken = default)
    {
        var observation = await sessions.TryObserveActiveTargetsAsync(cancellationToken);
        return observation is null ? null : targets.Apply(observation);
    }
}