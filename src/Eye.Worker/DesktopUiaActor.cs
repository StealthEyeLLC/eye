using System.Windows.Automation;
using StealthEye.Contract;

namespace StealthEye.Worker;

internal static class DesktopUiaActor
{
    internal static WorkerUiaActionResult Act(WorkerUiaActionRequest request)
    {
        if (request.Hwnd == 0)
            throw new ArgumentException("hwnd is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.RuntimeId))
            throw new ArgumentException("runtime_id is required.", nameof(request));
        if (request.MaxNodes is < 1 or > 20000)
            throw new ArgumentException("max_nodes must be between 1 and 20000.", nameof(request));
        if (request.Action is not ("focus" or "invoke" or "set_value"))
            throw new ArgumentException("action must be focus, invoke, or set_value.", nameof(request));
        if (request.Action == "set_value" && request.Value is null)
            throw new ArgumentException("value is required for set_value.", nameof(request));

        var root = AutomationElement.FromHandle(new IntPtr(request.Hwnd))
            ?? throw new ArgumentException("Window has no UI Automation root.", nameof(request));
        var element = FindByRuntimeId(root, request.RuntimeId, request.MaxNodes)
            ?? throw new ArgumentException("UI Automation element is no longer available.", nameof(request));

        switch (request.Action)
        {
            case "focus":
                element.SetFocus();
                break;
            case "invoke":
                if (!element.TryGetCurrentPattern(InvokePattern.Pattern, out var invokeObject))
                    throw new InvalidOperationException("Element does not support InvokePattern.");
                ((InvokePattern)invokeObject).Invoke();
                break;
            case "set_value":
                if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObject))
                    throw new InvalidOperationException("Element does not support ValuePattern.");
                var valuePattern = (ValuePattern)valueObject;
                if (valuePattern.Current.IsReadOnly)
                    throw new InvalidOperationException("Element ValuePattern is read-only.");
                valuePattern.SetValue(request.Value!);
                break;
        }

        return new WorkerUiaActionResult(request.Action, true);
    }

    private static AutomationElement? FindByRuntimeId(AutomationElement root, string runtimeId, int maxNodes)
    {
        var queue = new Queue<AutomationElement>();
        queue.Enqueue(root);
        var visited = 0;
        while (queue.Count > 0 && visited < maxNodes)
        {
            var current = queue.Dequeue();
            visited++;
            try
            {
                if (string.Equals(RuntimeId(current), runtimeId, StringComparison.Ordinal))
                    return current;
                var children = current.FindAll(TreeScope.Children, Condition.TrueCondition);
                foreach (AutomationElement child in children)
                    queue.Enqueue(child);
            }
            catch (ElementNotAvailableException)
            {
            }
        }
        return null;
    }

    private static string RuntimeId(AutomationElement element)
    {
        var runtimeId = element.GetRuntimeId();
        return runtimeId is { Length: > 0 } ? string.Join('.', runtimeId) : string.Empty;
    }
}