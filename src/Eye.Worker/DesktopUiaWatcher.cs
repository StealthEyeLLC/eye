using System.Windows.Automation;
using StealthEye.Contract;

namespace StealthEye.Worker;

internal sealed class DesktopUiaWatcher : IDisposable
{
    private static readonly string[] DefaultEvents = ["structure", "property", "focus"];
    private readonly object _gate = new();
    private ActiveWatch? _active;

    internal WorkerUiaArmResult Arm(WorkerUiaWaitRequest request)
    {
        lock (_gate)
        {
            if (_active is not null)
                throw new InvalidOperationException("A UIA watch is already armed in this worker.");
            _active = ActiveWatch.Create(request);
            return new WorkerUiaArmResult(true);
        }
    }

    internal async Task<WorkerUiaChangeResult> WaitAsync(CancellationToken cancellationToken)
    {
        ActiveWatch active;
        lock (_gate)
            active = _active ?? throw new InvalidOperationException("No UIA watch is armed in this worker.");
        try
        {
            return await active.Completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_active, active))
                    _active = null;
            }
            active.Dispose();
        }
    }

    public void Dispose()
    {
        ActiveWatch? active;
        lock (_gate)
        {
            active = _active;
            _active = null;
        }
        active?.Dispose();
    }

    private sealed class ActiveWatch : IDisposable
    {
        private readonly AutomationElement _target;
        private readonly long _hwnd;
        private readonly string _targetRuntimeId;
        private readonly int _maxNodes;
        private StructureChangedEventHandler? _structureHandler;
        private AutomationPropertyChangedEventHandler? _propertyHandler;
        private AutomationFocusChangedEventHandler? _focusHandler;
        private AutomationPropertyChangedEventHandler? _focusPropertyHandler;
        private CancellationTokenSource? _focusPolling;
        private Task? _focusPollingTask;
        private int _disposed;

        private ActiveWatch(
            AutomationElement target,
            long hwnd,
            string targetRuntimeId,
            int maxNodes)
        {
            _target = target;
            _hwnd = hwnd;
            _targetRuntimeId = targetRuntimeId;
            _maxNodes = maxNodes;
        }

        internal TaskCompletionSource<WorkerUiaChangeResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal static ActiveWatch Create(WorkerUiaWaitRequest request)
        {
            if (request.Hwnd == 0)
                throw new ArgumentException("hwnd is required.", nameof(request));
            if (request.MaxNodes is < 1 or > 20000)
                throw new ArgumentException("max_nodes must be between 1 and 20000.", nameof(request));

            var eventTypes = request.EventTypes is { Length: > 0 } ? request.EventTypes : DefaultEvents;
            if (eventTypes.Any(x => x is not ("structure" or "property" or "focus")))
                throw new ArgumentException("event_types may contain only structure, property, or focus.", nameof(request));

            var root = AutomationElement.FromHandle(new IntPtr(request.Hwnd))
                ?? throw new ArgumentException("Window has no UI Automation root.", nameof(request));
            var target = string.IsNullOrWhiteSpace(request.RuntimeId)
                ? root
                : FindByRuntimeId(root, request.RuntimeId!, request.MaxNodes)
                  ?? throw new ArgumentException("UI Automation element is no longer available.", nameof(request));

            var targetRuntimeId = RuntimeId(target);
            var watch = new ActiveWatch(target, request.Hwnd, targetRuntimeId, request.MaxNodes);
            try
            {
                watch.Register(eventTypes);
                return watch;
            }
            catch
            {
                watch.Dispose();
                throw;
            }
        }

        private void Register(string[] eventTypes)
        {
            var targetRuntimeId = _targetRuntimeId;

            if (eventTypes.Contains("structure", StringComparer.Ordinal))
            {
                _structureHandler = (sender, args) =>
                {
                    if (sender is not AutomationElement element) return;
                    Completion.TrySetResult(new WorkerUiaChangeResult(
                        DateTimeOffset.UtcNow,
                        "structure",
                        RuntimeIdSafe(element),
                        "structure_change_type",
                        args.StructureChangeType.ToString()));
                };
                Automation.AddStructureChangedEventHandler(
                    _target,
                    TreeScope.Subtree,
                    _structureHandler);
            }

            if (eventTypes.Contains("property", StringComparer.Ordinal))
            {
                _propertyHandler = (sender, args) =>
                {
                    if (sender is not AutomationElement element) return;
                    Completion.TrySetResult(new WorkerUiaChangeResult(
                        DateTimeOffset.UtcNow,
                        "property",
                        RuntimeIdSafe(element),
                        args.Property.ProgrammaticName,
                        args.NewValue?.ToString()));
                };
                Automation.AddAutomationPropertyChangedEventHandler(
                    _target,
                    TreeScope.Subtree,
                    _propertyHandler,
                    AutomationElement.NameProperty,
                    AutomationElement.IsEnabledProperty,
                    AutomationElement.IsOffscreenProperty,
                    AutomationElement.HasKeyboardFocusProperty,
                    ValuePattern.ValueProperty,
                    TogglePattern.ToggleStateProperty,
                    SelectionItemPattern.IsSelectedProperty,
                    ExpandCollapsePattern.ExpandCollapseStateProperty);
            }

            if (eventTypes.Contains("focus", StringComparer.Ordinal))
            {
                _focusPropertyHandler = (sender, args) =>
                {
                    if (sender is not AutomationElement element) return;
                    if (args.NewValue is not bool hasFocus || !hasFocus) return;
                    if (!IsWithinTarget(element, targetRuntimeId, 64)) return;
                    Completion.TrySetResult(new WorkerUiaChangeResult(
                        DateTimeOffset.UtcNow,
                        "focus",
                        RuntimeIdSafe(element),
                        null,
                        null));
                };
                Automation.AddAutomationPropertyChangedEventHandler(
                    _target,
                    TreeScope.Subtree,
                    _focusPropertyHandler,
                    AutomationElement.HasKeyboardFocusProperty);

                _focusHandler = (_, _) =>
                {
                    AutomationElement? focused;
                    try { focused = AutomationElement.FocusedElement; }
                    catch { return; }
                    if (focused is null || !IsWithinTarget(focused, targetRuntimeId, 64)) return;
                    Completion.TrySetResult(new WorkerUiaChangeResult(
                        DateTimeOffset.UtcNow,
                        "focus",
                        RuntimeIdSafe(focused),
                        null,
                        null));
                };
                Automation.AddAutomationFocusChangedEventHandler(_focusHandler);

                var initiallyFocused = HasKeyboardFocusFresh(_hwnd, targetRuntimeId, _maxNodes);
                _focusPolling = new CancellationTokenSource();
                _focusPollingTask = Task.Run(
                    () => PollFocusAsync(
                        _hwnd,
                        targetRuntimeId,
                        _maxNodes,
                        initiallyFocused,
                        Completion,
                        _focusPolling.Token));
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            try { _focusPolling?.Cancel(); } catch { }
            try
            {
                if (_focusPollingTask is not null)
                    _focusPollingTask.Wait(TimeSpan.FromSeconds(1));
            }
            catch { }
            _focusPolling?.Dispose();

            try
            {
                if (_structureHandler is not null)
                    Automation.RemoveStructureChangedEventHandler(_target, _structureHandler);
            }
            catch { }

            try
            {
                if (_propertyHandler is not null)
                    Automation.RemoveAutomationPropertyChangedEventHandler(_target, _propertyHandler);
            }
            catch { }

            try
            {
                if (_focusPropertyHandler is not null)
                    Automation.RemoveAutomationPropertyChangedEventHandler(_target, _focusPropertyHandler);
            }
            catch { }

            try
            {
                if (_focusHandler is not null)
                    Automation.RemoveAutomationFocusChangedEventHandler(_focusHandler);
            }
            catch { }
        }
    }

    private static async Task PollFocusAsync(
        long hwnd,
        string targetRuntimeId,
        int maxNodes,
        bool previous,
        TaskCompletionSource<WorkerUiaChangeResult> completion,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !completion.Task.IsCompleted)
        {
            try
            {
                await Task.Delay(25, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var current = HasKeyboardFocusFresh(hwnd, targetRuntimeId, maxNodes);
            if (current && !previous)
            {
                completion.TrySetResult(new WorkerUiaChangeResult(
                    DateTimeOffset.UtcNow,
                    "focus",
                    targetRuntimeId,
                    null,
                    null));
                break;
            }

            previous = current;
        }
    }

    private static bool HasKeyboardFocusFresh(long hwnd, string runtimeId, int maxNodes)
    {
        try
        {
            var root = AutomationElement.FromHandle(new IntPtr(hwnd));
            if (root is null)
                return false;
            var target = FindByRuntimeId(root, runtimeId, maxNodes);
            return target is not null && target.Current.HasKeyboardFocus;
        }
        catch
        {
            return false;
        }
    }
    private static bool HasKeyboardFocusSafe(AutomationElement element)
    {
        try { return element.Current.HasKeyboardFocus; }
        catch { return false; }
    }

    private static bool IsWithinTarget(
        AutomationElement element,
        string targetRuntimeId,
        int maxAncestors)
    {
        var current = element;
        for (var i = 0; i <= maxAncestors && current is not null; i++)
        {
            if (string.Equals(RuntimeIdSafe(current), targetRuntimeId, StringComparison.Ordinal))
                return true;
            try { current = TreeWalker.ControlViewWalker.GetParent(current); }
            catch { return false; }
        }
        return false;
    }

    private static AutomationElement? FindByRuntimeId(
        AutomationElement root,
        string runtimeId,
        int maxNodes)
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
        if (runtimeId is not { Length: > 0 })
            throw new InvalidOperationException("UI Automation element has no runtime ID.");
        return string.Join('.', runtimeId);
    }

    private static string RuntimeIdSafe(AutomationElement element)
    {
        try { return RuntimeId(element); }
        catch { return string.Empty; }
    }
}
