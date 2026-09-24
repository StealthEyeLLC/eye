using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace StealthEye.Runtime;

internal sealed class ProcessStartupAttributeList : IDisposable
{
    private IntPtr _attributeList;
    private IntPtr _attributeValue;
    private IntPtr _desktop;
    private int _disposed;

    private ProcessStartupAttributeList(
        IntPtr attributeList,
        IntPtr attributeValue,
        IntPtr desktop,
        NativeMethods.STARTUPINFOEX_NATIVE startupInfo)
    {
        _attributeList = attributeList;
        _attributeValue = attributeValue;
        _desktop = desktop;
        StartupInfo = startupInfo;
    }

    internal NativeMethods.STARTUPINFOEX_NATIVE StartupInfo { get; }

    internal static ProcessStartupAttributeList CreateStdio(
        SafeFileHandle stdin,
        SafeFileHandle stdout,
        SafeFileHandle stderr,
        string? desktop = null)
    {
        ArgumentNullException.ThrowIfNull(stdin);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        if (stdin.IsInvalid || stdin.IsClosed)
            throw new ArgumentException("stdin handle is invalid or closed.", nameof(stdin));
        if (stdout.IsInvalid || stdout.IsClosed)
            throw new ArgumentException("stdout handle is invalid or closed.", nameof(stdout));
        if (stderr.IsInvalid || stderr.IsClosed)
            throw new ArgumentException("stderr handle is invalid or closed.", nameof(stderr));

        IntPtr attributeList = IntPtr.Zero;
        IntPtr attributeValue = IntPtr.Zero;
        IntPtr desktopPointer = IntPtr.Zero;
        try
        {
            nuint bytes = 0;
            NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref bytes);
            if (bytes == 0)
                ProcessRunner.ThrowWin32("InitializeProcThreadAttributeList(HANDLE_LIST size)");

            attributeList = Marshal.AllocHGlobal(checked((int)bytes));
            if (!NativeMethods.InitializeProcThreadAttributeList(attributeList, 1, 0, ref bytes))
                ProcessRunner.ThrowWin32("InitializeProcThreadAttributeList(HANDLE_LIST)");

            var handleBytes = checked(3 * IntPtr.Size);
            attributeValue = Marshal.AllocHGlobal(handleBytes);
            Marshal.WriteIntPtr(attributeValue, 0, stdin.DangerousGetHandle());
            Marshal.WriteIntPtr(attributeValue, IntPtr.Size, stdout.DangerousGetHandle());
            Marshal.WriteIntPtr(attributeValue, 2 * IntPtr.Size, stderr.DangerousGetHandle());

            if (!NativeMethods.UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    NativeMethods.PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
                    attributeValue,
                    checked((nuint)handleBytes),
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                ProcessRunner.ThrowWin32("UpdateProcThreadAttribute(HANDLE_LIST)");
            }

            if (!string.IsNullOrWhiteSpace(desktop))
                desktopPointer = Marshal.StringToHGlobalUni(desktop);

            var startup = new NativeMethods.STARTUPINFOEX_NATIVE
            {
                StartupInfo = new NativeMethods.STARTUPINFO_NATIVE
                {
                    cb = (uint)Marshal.SizeOf<NativeMethods.STARTUPINFOEX_NATIVE>(),
                    lpDesktop = desktopPointer,
                    dwFlags = NativeMethods.STARTF_USESTDHANDLES,
                    hStdInput = stdin.DangerousGetHandle(),
                    hStdOutput = stdout.DangerousGetHandle(),
                    hStdError = stderr.DangerousGetHandle()
                },
                lpAttributeList = attributeList
            };

            var owner = new ProcessStartupAttributeList(
                attributeList,
                attributeValue,
                desktopPointer,
                startup);
            attributeList = IntPtr.Zero;
            attributeValue = IntPtr.Zero;
            desktopPointer = IntPtr.Zero;
            return owner;
        }
        catch
        {
            if (attributeList != IntPtr.Zero)
            {
                NativeMethods.DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            if (attributeValue != IntPtr.Zero)
                Marshal.FreeHGlobal(attributeValue);
            if (desktopPointer != IntPtr.Zero)
                Marshal.FreeHGlobal(desktopPointer);
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (_attributeList != IntPtr.Zero)
        {
            NativeMethods.DeleteProcThreadAttributeList(_attributeList);
            Marshal.FreeHGlobal(_attributeList);
            _attributeList = IntPtr.Zero;
        }

        if (_attributeValue != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_attributeValue);
            _attributeValue = IntPtr.Zero;
        }

        if (_desktop != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_desktop);
            _desktop = IntPtr.Zero;
        }
    }
}