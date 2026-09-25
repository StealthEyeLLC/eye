using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using StealthEye.Contract;

namespace StealthEye.Worker;

internal static class DesktopWindowInventory
{
    private static readonly IntPtr PerMonitorV2 = new(-4);

    internal static void EnablePerMonitorV2()
    {
        if (!OperatingSystem.IsWindows()) return;
        _ = Native.SetProcessDpiAwarenessContext(PerMonitorV2);
    }

    internal static WorkerDesktopObservationResult Observe(bool includeInvisible)
    {
        var foreground = Native.GetForegroundWindow();
        var windows = new List<WorkerWindowInfo>();
        Native.EnumWindows((hwnd, _) =>
        {
            var visible = Native.IsWindowVisible(hwnd);
            if (!includeInvisible && !visible) return true;
            if (!Native.GetWindowRect(hwnd, out var rect)) return true;

            var title = GetTitle(hwnd);
            var className = GetClassName(hwnd);
            var threadId = unchecked((int)Native.GetWindowThreadProcessId(hwnd, out var rawPid));
            var processId = unchecked((int)rawPid);
            string processName = string.Empty;
            DateTimeOffset? processStart = null;
            if (processId > 0)
            {
                try
                {
                    using var process = Process.GetProcessById(processId);
                    processName = process.ProcessName;
                    processStart = process.StartTime.ToUniversalTime();
                }
                catch
                {
                }
            }

            windows.Add(new WorkerWindowInfo(
                hwnd.ToInt64(),
                processId,
                processStart,
                processName,
                threadId,
                title,
                className,
                visible,
                Native.IsIconic(hwnd),
                hwnd == foreground,
                new WorkerWindowRect(rect.Left, rect.Top, rect.Right, rect.Bottom),
                DesktopUiaRootReader.TryRead(hwnd)));
            return true;
        }, IntPtr.Zero);

        var sessionId = Process.GetCurrentProcess().SessionId;
        var sessionState = DesktopSessionState.Observe(sessionId);
        return new WorkerDesktopObservationResult(
            sessionId,
            sessionState.Locked,
            sessionState.SecureDesktop,
            sessionState.InputDesktopAccessible,
            sessionState.InputDesktopName,
            DateTimeOffset.UtcNow,
            [.. windows]);
    }

    private static string GetTitle(IntPtr hwnd)
    {
        var length = Native.GetWindowTextLengthW(hwnd);
        if (length <= 0) return string.Empty;
        var text = new StringBuilder(length + 1);
        return Native.GetWindowTextW(hwnd, text, text.Capacity) > 0 ? text.ToString() : string.Empty;
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var text = new StringBuilder(256);
        return Native.GetClassNameW(hwnd, text, text.Capacity) > 0 ? text.ToString() : string.Empty;
    }

    private static class Native
    {
        internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowTextLengthW(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int count);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassNameW(IntPtr hwnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    }
}
