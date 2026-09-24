using System.Windows.Automation;
using StealthEye.Contract;

namespace StealthEye.Worker;

internal static class DesktopUiaTreeReader
{
    internal static WorkerUiaQueryResult Query(WorkerUiaQueryRequest request)
    {
        if (request.Hwnd == 0)
            throw new ArgumentException("hwnd is required.", nameof(request));
        if (request.MaxDepth is < 0 or > 12)
            throw new ArgumentException("max_depth must be between 0 and 12.", nameof(request));
        if (request.MaxNodes is < 1 or > 2000)
            throw new ArgumentException("max_nodes must be between 1 and 2000.", nameof(request));

        var root = AutomationElement.FromHandle(new IntPtr(request.Hwnd))
            ?? throw new ArgumentException("Window has no UI Automation root.", nameof(request));
        var cache = CreateCacheRequest();
        var rootCached = Update(root, cache);
        var queue = new Queue<(AutomationElement Element, string? ParentRuntimeId, int Depth)>();
        queue.Enqueue((rootCached, null, 0));
        var elements = new List<WorkerUiaElementInfo>(Math.Min(request.MaxNodes, 256));
        var truncated = false;

        while (queue.Count > 0 && elements.Count < request.MaxNodes)
        {
            var current = queue.Dequeue();
            WorkerUiaElementInfo info;
            try
            {
                info = Snapshot(current.Element, current.ParentRuntimeId, current.Depth);
            }
            catch (ElementNotAvailableException)
            {
                continue;
            }
            elements.Add(info);

            if (current.Depth >= request.MaxDepth)
            {
                if (current.Depth == request.MaxDepth)
                    truncated = true;
                continue;
            }

            AutomationElementCollection children;
            try
            {
                using (cache.Activate())
                    children = current.Element.FindAll(TreeScope.Children, Condition.TrueCondition);
            }
            catch (ElementNotAvailableException)
            {
                continue;
            }

            foreach (AutomationElement child in children)
            {
                queue.Enqueue((child, info.RuntimeId, current.Depth + 1));
                if (elements.Count + queue.Count >= request.MaxNodes)
                {
                    truncated = true;
                    break;
                }
            }
        }

        if (queue.Count > 0) truncated = true;
        return new WorkerUiaQueryResult(DateTimeOffset.UtcNow, truncated, [.. elements]);
    }

    private static CacheRequest CreateCacheRequest()
    {
        var request = new CacheRequest { TreeScope = TreeScope.Element };
        request.Add(AutomationElement.NameProperty);
        request.Add(AutomationElement.AutomationIdProperty);
        request.Add(AutomationElement.ControlTypeProperty);
        request.Add(AutomationElement.FrameworkIdProperty);
        request.Add(AutomationElement.ClassNameProperty);
        request.Add(AutomationElement.IsEnabledProperty);
        request.Add(AutomationElement.IsOffscreenProperty);
        request.Add(AutomationElement.HasKeyboardFocusProperty);
        request.Add(AutomationElement.BoundingRectangleProperty);
        return request;
    }

    private static AutomationElement Update(AutomationElement element, CacheRequest request)
    {
        using (request.Activate())
            return element.GetUpdatedCache(request);
    }

    private static WorkerUiaElementInfo Snapshot(AutomationElement element, string? parentRuntimeId, int depth)
    {
        var cached = element.Cached;
        var bounds = cached.BoundingRectangle;
        return new WorkerUiaElementInfo(
            RuntimeId(element),
            parentRuntimeId,
            depth,
            cached.Name ?? string.Empty,
            cached.AutomationId ?? string.Empty,
            cached.ControlType?.ProgrammaticName ?? string.Empty,
            cached.FrameworkId ?? string.Empty,
            cached.ClassName ?? string.Empty,
            cached.IsEnabled,
            cached.IsOffscreen,
            cached.HasKeyboardFocus,
            new WorkerWindowRect(
                Coordinate(bounds.Left),
                Coordinate(bounds.Top),
                Coordinate(bounds.Right),
                Coordinate(bounds.Bottom)));
    }

    private static int Coordinate(double value) =>
        double.IsFinite(value) ? (int)Math.Clamp(Math.Round(value), int.MinValue, int.MaxValue) : 0;

    private static string RuntimeId(AutomationElement element)
    {
        var runtimeId = element.GetRuntimeId();
        if (runtimeId is null || runtimeId.Length == 0)
            throw new InvalidOperationException("UI Automation element has no runtime ID.");
        return string.Join('.', runtimeId);
    }
}