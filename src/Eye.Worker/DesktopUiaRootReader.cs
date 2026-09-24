using System.Windows.Automation;
using StealthEye.Contract;

namespace StealthEye.Worker;

internal static class DesktopUiaRootReader
{
    internal static WorkerUiaWindowRoot? TryRead(IntPtr hwnd)
    {
        try
        {
            var element = AutomationElement.FromHandle(hwnd);
            if (element is null) return null;

            var request = new CacheRequest { TreeScope = TreeScope.Element };
            request.Add(AutomationElement.NameProperty);
            request.Add(AutomationElement.AutomationIdProperty);
            request.Add(AutomationElement.ControlTypeProperty);
            request.Add(AutomationElement.FrameworkIdProperty);
            request.Add(AutomationElement.ClassNameProperty);
            request.Add(AutomationElement.IsEnabledProperty);
            request.Add(AutomationElement.IsOffscreenProperty);

            using (request.Activate())
            {
                var cached = element.GetUpdatedCache(request).Cached;
                return new WorkerUiaWindowRoot(
                    cached.Name ?? string.Empty,
                    cached.AutomationId ?? string.Empty,
                    cached.ControlType?.ProgrammaticName ?? string.Empty,
                    cached.FrameworkId ?? string.Empty,
                    cached.ClassName ?? string.Empty,
                    cached.IsEnabled,
                    cached.IsOffscreen);
            }
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}