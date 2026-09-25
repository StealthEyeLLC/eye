using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using StealthEye.Contract;

namespace StealthEye.Worker;

internal static class DesktopContextProbe
{
    private const uint CfUnicodeText = 13;

    internal static WorkerDesktopContextResult Observe()
    {
        var foreground = Native.GetForegroundWindow();
        var processPath = ForegroundProcessPath(foreground);
        var clipboard = ReadClipboardText();
        var selection = ReadFocusedSelection();
        var (explorerPath, selectedPaths) = ReadExplorerContext(foreground);

        return new WorkerDesktopContextResult(
            DateTimeOffset.UtcNow,
            clipboard,
            selection,
            processPath,
            explorerPath,
            selectedPaths);
    }

    private static string? ForegroundProcessPath(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return null;
        _ = Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
            return null;

        try
        {
            using var process = Process.GetProcessById(checked((int)pid));
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadClipboardText()
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (Native.OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    if (!Native.IsClipboardFormatAvailable(CfUnicodeText))
                        return null;

                    var handle = Native.GetClipboardData(CfUnicodeText);
                    if (handle == IntPtr.Zero)
                        return null;

                    var pointer = Native.GlobalLock(handle);
                    if (pointer == IntPtr.Zero)
                        return null;
                    try
                    {
                        var text = Marshal.PtrToStringUni(pointer);
                        return Bound(text, 8192);
                    }
                    finally
                    {
                        _ = Native.GlobalUnlock(handle);
                    }
                }
                finally
                {
                    _ = Native.CloseClipboard();
                }
            }

            Thread.Sleep(25);
        }

        return null;
    }

    private static string? ReadFocusedSelection()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null)
                return null;

            if (focused.TryGetCurrentPattern(TextPattern.Pattern, out var textObject))
            {
                var text = (TextPattern)textObject;
                var selected = text.GetSelection();
                var combined = string.Join(
                    Environment.NewLine,
                    selected.Select(x => x.GetText(4096)).Where(x => !string.IsNullOrWhiteSpace(x)));
                if (!string.IsNullOrWhiteSpace(combined))
                    return Bound(combined, 8192);
            }

            if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObject))
            {
                var value = ((ValuePattern)valueObject).Current.Value;
                if (!string.IsNullOrWhiteSpace(value))
                    return Bound(value, 8192);
            }

            var name = focused.Current.Name;
            return string.IsNullOrWhiteSpace(name) ? null : Bound(name, 8192);
        }
        catch
        {
            return null;
        }
    }

    private static (string? Path, string[] SelectedPaths) ReadExplorerContext(IntPtr foreground)
    {
        object? shell = null;
        object? windows = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
                return (null, []);

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
                return (null, []);

            windows = shellType.InvokeMember(
                "Windows",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: null);

            if (windows is null)
                return (null, []);

            var count = Convert.ToInt32(
                windows.GetType().InvokeMember(
                    "Count",
                    System.Reflection.BindingFlags.GetProperty,
                    binder: null,
                    target: windows,
                    args: null));

            for (var index = 0; index < count; index++)
            {
                object? window = null;
                try
                {
                    window = windows.GetType().InvokeMember(
                        "Item",
                        System.Reflection.BindingFlags.InvokeMethod,
                        binder: null,
                        target: windows,
                        args: [index]);
                    if (window is null)
                        continue;

                    var hwnd = Convert.ToInt64(
                        window.GetType().InvokeMember(
                            "HWND",
                            System.Reflection.BindingFlags.GetProperty,
                            binder: null,
                            target: window,
                            args: null));

                    if (hwnd != foreground.ToInt64())
                        continue;

                    var document = window.GetType().InvokeMember(
                        "Document",
                        System.Reflection.BindingFlags.GetProperty,
                        binder: null,
                        target: window,
                        args: null);
                    if (document is null)
                        return (null, []);

                    var folder = document.GetType().InvokeMember(
                        "Folder",
                        System.Reflection.BindingFlags.GetProperty,
                        binder: null,
                        target: document,
                        args: null);
                    var self = folder?.GetType().InvokeMember(
                        "Self",
                        System.Reflection.BindingFlags.GetProperty,
                        binder: null,
                        target: folder,
                        args: null);
                    var path = self?.GetType().InvokeMember(
                        "Path",
                        System.Reflection.BindingFlags.GetProperty,
                        binder: null,
                        target: self,
                        args: null) as string;

                    var selected = document.GetType().InvokeMember(
                        "SelectedItems",
                        System.Reflection.BindingFlags.InvokeMethod,
                        binder: null,
                        target: document,
                        args: null);
                    var selectedPaths = new List<string>();
                    if (selected is not null)
                    {
                        var selectedCount = Convert.ToInt32(
                            selected.GetType().InvokeMember(
                                "Count",
                                System.Reflection.BindingFlags.GetProperty,
                                binder: null,
                                target: selected,
                                args: null));
                        for (var selectedIndex = 0; selectedIndex < Math.Min(selectedCount, 64); selectedIndex++)
                        {
                            var item = selected.GetType().InvokeMember(
                                "Item",
                                System.Reflection.BindingFlags.InvokeMethod,
                                binder: null,
                                target: selected,
                                args: [selectedIndex]);
                            var selectedPath = item?.GetType().InvokeMember(
                                "Path",
                                System.Reflection.BindingFlags.GetProperty,
                                binder: null,
                                target: item,
                                args: null) as string;
                            if (!string.IsNullOrWhiteSpace(selectedPath))
                                selectedPaths.Add(selectedPath);
                            Release(item);
                        }
                        Release(selected);
                    }

                    Release(self);
                    Release(folder);
                    Release(document);
                    return (path, [.. selectedPaths]);
                }
                finally
                {
                    Release(window);
                }
            }
        }
        catch
        {
            return (null, []);
        }
        finally
        {
            Release(windows);
            Release(shell);
        }

        return (null, []);
    }

    private static string? Bound(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars];
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try { _ = Marshal.FinalReleaseComObject(value); }
            catch { }
        }
    }

    private static class Native
    {
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenClipboard(IntPtr owner);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetClipboardData(uint format);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr GlobalLock(IntPtr memory);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalUnlock(IntPtr memory);
    }
}