namespace StealthEye.Runtime;

public sealed class UiaActionService(
    DesktopWindowStore windows,
    UiaElementStore elements,
    SessionWorkerManager workers)
{
    public async Task<UiaActionSnapshot> ActAsync(
        string elementId,
        string action,
        string? value = null,
        CancellationToken cancellationToken = default)
    {
        if (action is not ("focus" or "invoke" or "set_value"))
            throw new ArgumentException("action must be focus, invoke, or set_value.", nameof(action));
        if (action == "set_value" && value is null)
            throw new ArgumentException("value is required for set_value.", nameof(value));
        if (action != "set_value" && value is not null)
            throw new ArgumentException("value is only valid for set_value.", nameof(value));

        var element = elements.ResolveActive(elementId);
        var window = windows.ResolveActive(element.WindowId);
        if (window.Incarnation != element.WindowIncarnation)
            throw new ArgumentException($"Element {elementId} belongs to an older window incarnation.", nameof(elementId));

        var result = await workers.ActUiaAsync(
            window.Hwnd,
            element.RuntimeId,
            action,
            value,
            cancellationToken);
        return new UiaActionSnapshot(
            element.ElementId,
            element.Incarnation,
            result.Action,
            result.Completed,
            DateTimeOffset.UtcNow);
    }
}