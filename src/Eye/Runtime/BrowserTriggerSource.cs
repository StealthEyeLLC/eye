using System.Text.Json;

namespace StealthEye.Runtime;

public sealed record BrowserNavigationTriggerEvent(
    DateTimeOffset OccurredAt,
    string TargetId,
    long TargetIncarnation,
    string Url,
    string? FrameId,
    string? LoaderId);

public sealed class BrowserTriggerSource(
    BrowserSessionManager sessions,
    BrowserTargetStore targets)
{
    public string PrepareNavigationRegistration(string targetId)
    {
        var target = targets.ResolveActive(targetId);
        if (!string.Equals(target.Type, "page", StringComparison.Ordinal))
            throw new ArgumentException("Browser navigation triggers require a page target.", nameof(targetId));

        return JsonSerializer.Serialize(new Registration(
            target.TargetId,
            target.Incarnation,
            target.CdpTargetId));
    }

    public async Task<BrowserNavigationTriggerEvent> WaitNavigationAsync(
        TriggerRecord trigger,
        Action? onArmed,
        CancellationToken cancellationToken)
    {
        if (trigger.Kind != TriggerKinds.BrowserNavigation ||
            string.IsNullOrWhiteSpace(trigger.RegistrationJson))
            throw new InvalidOperationException("Browser navigation trigger registration is missing.");

        var registration = JsonSerializer.Deserialize<Registration>(trigger.RegistrationJson)
            ?? throw new InvalidOperationException("Browser navigation trigger registration is invalid.");
        var target = targets.ResolveActive(registration.TargetId);
        if (target.Incarnation != registration.TargetIncarnation ||
            !string.Equals(target.CdpTargetId, registration.CdpTargetId, StringComparison.Ordinal))
            throw new InvalidOperationException("Browser navigation trigger target incarnation is stale.");

        await sessions.ArmNavigationAsync(target.CdpTargetId, cancellationToken);
        onArmed?.Invoke();
        var navigation = await sessions.WaitNavigationAsync(cancellationToken);
        if (!string.Equals(navigation.CdpTargetId, target.CdpTargetId, StringComparison.Ordinal))
            throw new InvalidOperationException("Browser navigation watcher returned an unexpected target.");

        return new BrowserNavigationTriggerEvent(
            navigation.OccurredAt,
            target.TargetId,
            target.Incarnation,
            navigation.Url,
            navigation.FrameId,
            navigation.LoaderId);
    }

    private sealed record Registration(
        string TargetId,
        long TargetIncarnation,
        string CdpTargetId);
}
