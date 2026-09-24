using System.Text.Json;

namespace StealthEye.Runtime;

public sealed record UiaTriggerEvent(
    DateTimeOffset OccurredAt,
    string EventType,
    string WindowId,
    long WindowIncarnation,
    string? ElementId,
    long? ElementIncarnation,
    string? Property,
    string? Value);

public sealed class UiaTriggerSource(
    DesktopWindowStore windowStore,
    UiaElementStore elementStore,
    SessionWorkerManager workers)
{
    private static readonly string[] DefaultEvents = ["structure", "property", "focus"];

    public string PrepareRegistration(string windowId, string? elementId, string[]? eventTypes)
    {
        var window = windowStore.ResolveActive(windowId);
        UiaElementTarget? element = null;
        if (!string.IsNullOrWhiteSpace(elementId))
        {
            element = elementStore.ResolveActive(elementId);
            if (!string.Equals(element.WindowId, window.WindowId, StringComparison.Ordinal) || element.WindowIncarnation != window.Incarnation)
                throw new ArgumentException("element_id does not belong to the active window incarnation.", nameof(elementId));
        }

        var normalized = NormalizeEvents(eventTypes);
        return JsonSerializer.Serialize(new Registration(
            window.WindowId,
            window.Incarnation,
            element?.ElementId,
            element?.Incarnation,
            normalized));
    }

    public async Task<UiaTriggerEvent> WaitAsync(TriggerRecord trigger, Action? onArmed, CancellationToken cancellationToken)
    {
        if (trigger.Kind != TriggerKinds.UiaChange || string.IsNullOrWhiteSpace(trigger.RegistrationJson))
            throw new InvalidOperationException("UIA trigger registration is missing.");
        var registration = JsonSerializer.Deserialize<Registration>(trigger.RegistrationJson)
            ?? throw new InvalidOperationException("UIA trigger registration is invalid.");
        var window = windowStore.ResolveActive(registration.WindowId);
        if (window.Incarnation != registration.WindowIncarnation)
            throw new InvalidOperationException("UIA trigger window incarnation is stale.");

        string? runtimeId = null;
        if (registration.ElementId is not null)
        {
            var element = elementStore.ResolveActive(registration.ElementId);
            if (element.Incarnation != registration.ElementIncarnation ||
                !string.Equals(element.WindowId, window.WindowId, StringComparison.Ordinal) ||
                element.WindowIncarnation != window.Incarnation)
                throw new InvalidOperationException("UIA trigger element incarnation is stale.");
            runtimeId = element.RuntimeId;
        }

        var change = await workers.WaitUiaChangeAsync(
            window.Hwnd,
            runtimeId,
            registration.EventTypes,
            onArmed: onArmed,
            cancellationToken: cancellationToken);
        var mapped = elementStore.TryResolveActiveByRuntimeId(window.WindowId, change.RuntimeId);
        if (mapped is not null && mapped.WindowIncarnation != window.Incarnation)
            mapped = null;

        return new UiaTriggerEvent(
            change.OccurredAt,
            change.EventType,
            window.WindowId,
            window.Incarnation,
            mapped?.ElementId,
            mapped?.Incarnation,
            change.Property,
            change.Value);
    }

    private static string[] NormalizeEvents(string[]? eventTypes)
    {
        var normalized = eventTypes is { Length: > 0 }
            ? eventTypes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
            : DefaultEvents;
        if (normalized.Any(x => x is not ("structure" or "property" or "focus")))
            throw new ArgumentException("event_types may contain only structure, property, or focus.", nameof(eventTypes));
        return normalized;
    }

    private sealed record Registration(
        string WindowId,
        long WindowIncarnation,
        string? ElementId,
        long? ElementIncarnation,
        string[] EventTypes);
}