using System.Runtime.InteropServices;
using System.Text;

namespace StealthEye.Worker;

internal sealed record DesktopSessionStateSnapshot(
    bool Locked,
    bool SecureDesktop,
    bool InputDesktopAccessible,
    string? InputDesktopName);

internal static class DesktopSessionState
{
    private const int WtsSessionInfoEx = 25;
    private const int WtsSessionStateLock = 0;
    private const int WtsSessionStateUnlock = 1;
    private const int UoiName = 2;
    private const uint DesktopReadObjects = 0x0001;

    internal static DesktopSessionStateSnapshot Observe(int sessionId)
    {
        var locked = QueryLocked(sessionId);
        var desktop = QueryInputDesktop();
        var secure = desktop.Name is not null &&
            !string.Equals(desktop.Name, "Default", StringComparison.OrdinalIgnoreCase);

        return new DesktopSessionStateSnapshot(
            locked,
            secure,
            desktop.Accessible,
            desktop.Name);
    }

    private static bool QueryLocked(int sessionId)
    {
        if (!Native.WTSQuerySessionInformationW(
                IntPtr.Zero,
                sessionId,
                WtsSessionInfoEx,
                out var buffer,
                out _))
            return false;

        try
        {
            var info = Marshal.PtrToStructure<WTSINFOEX>(buffer);
            if (info.Level != 1)
                return false;

            return info.Data.SessionFlags switch
            {
                WtsSessionStateLock => true,
                WtsSessionStateUnlock => false,
                _ => false
            };
        }
        finally
        {
            Native.WTSFreeMemory(buffer);
        }
    }

    private static (bool Accessible, string? Name) QueryInputDesktop()
    {
        var desktop = Native.OpenInputDesktop(0, false, DesktopReadObjects);
        if (desktop == IntPtr.Zero)
            return (false, null);

        try
        {
            var name = new StringBuilder(256);
            if (!Native.GetUserObjectInformationW(
                    desktop,
                    UoiName,
                    name,
                    checked((uint)(name.Capacity * sizeof(char))),
                    out _))
                return (true, null);

            return (true, name.ToString());
        }
        finally
        {
            Native.CloseDesktop(desktop);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WTSINFOEX
    {
        internal int Level;
        internal WTSINFOEX_LEVEL1 Data;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WTSINFOEX_LEVEL1
    {
        internal int SessionId;
        internal int SessionState;
        internal int SessionFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
        internal string WinStationName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
        internal string UserName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 18)]
        internal string DomainName;

        internal long LogonTime;
        internal long ConnectTime;
        internal long DisconnectTime;
        internal long LastInputTime;
        internal long CurrentTime;
        internal uint IncomingBytes;
        internal uint OutgoingBytes;
        internal uint IncomingFrames;
        internal uint OutgoingFrames;
        internal uint IncomingCompressedBytes;
        internal uint OutgoingCompressedBytes;
    }

    private static class Native
    {
        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WTSQuerySessionInformationW(
            IntPtr server,
            int sessionId,
            int infoClass,
            out IntPtr buffer,
            out int bytesReturned);

        [DllImport("wtsapi32.dll")]
        internal static extern void WTSFreeMemory(IntPtr memory);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr OpenInputDesktop(
            uint flags,
            [MarshalAs(UnmanagedType.Bool)] bool inherit,
            uint desiredAccess);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetUserObjectInformationW(
            IntPtr handle,
            int index,
            StringBuilder info,
            uint length,
            out uint needed);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseDesktop(IntPtr desktop);
    }
}