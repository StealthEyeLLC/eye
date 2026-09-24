using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using StealthEye.Runtime;

namespace Eye.Tests;

[Collection("Interactive desktop")]
public sealed class HandleInheritanceTests
{
    [Fact]
    public void ExplicitHandleList_InheritsStdioButNotUnrelatedInheritableHandle()
    {
        var sa = new NativeMethods.SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<NativeMethods.SECURITY_ATTRIBUTES>(),
            bInheritHandle = true
        };

        Assert.True(NativeMethods.CreatePipe(out var stdoutRead, out var stdoutWrite, ref sa, 0));
        Assert.True(NativeMethods.CreatePipe(out var stderrRead, out var stderrWrite, ref sa, 0));
        Assert.True(NativeMethods.CreatePipe(out var stdinRead, out var stdinWrite, ref sa, 0));

        using var stdoutReadHandle = stdoutRead;
        using var stdoutWriteHandle = stdoutWrite;
        using var stderrReadHandle = stderrRead;
        using var stderrWriteHandle = stderrWrite;
        using var stdinReadHandle = stdinRead;
        using var stdinWriteHandle = stdinWrite;

        Assert.True(NativeMethods.SetHandleInformation(
            stdoutReadHandle,
            NativeMethods.HANDLE_FLAG_INHERIT,
            0));
        Assert.True(NativeMethods.SetHandleInformation(
            stderrReadHandle,
            NativeMethods.HANDLE_FLAG_INHERIT,
            0));
        Assert.True(NativeMethods.SetHandleInformation(
            stdinWriteHandle,
            NativeMethods.HANDLE_FLAG_INHERIT,
            0));

        using var unrelated = new EventWaitHandle(false, EventResetMode.ManualReset);
        Assert.True(SetHandleInformation(
            unrelated.SafeWaitHandle.DangerousGetHandle(),
            NativeMethods.HANDLE_FLAG_INHERIT,
            NativeMethods.HANDLE_FLAG_INHERIT));

        using var attributes = ProcessStartupAttributeList.CreateStdio(
            stdinReadHandle,
            stdoutWriteHandle,
            stderrWriteHandle);
        var startup = attributes.StartupInfo;

        var unrelatedValue = unrelated.SafeWaitHandle.DangerousGetHandle().ToInt64();
        var probe = string.Join(
            ";",
            "$src='using System; using System.Runtime.InteropServices; public static class EyeHandleProbe { [DllImport(\"kernel32.dll\", SetLastError=true)] public static extern bool SetEvent(IntPtr h); }'",
            "Add-Type -TypeDefinition $src",
            "Write-Output 'stdio-ok'",
            $"[void][EyeHandleProbe]::SetEvent([IntPtr]{unrelatedValue})",
            "exit 0");

        var executable = ProcessRunner.ResolveExecutable("powershell.exe")
            ?? throw new FileNotFoundException("powershell.exe was not found.");
        var commandLine = new StringBuilder(ProcessRunner.BuildCommandLine(
            executable,
            ["-NoLogo", "-NoProfile", "-Command", probe]));

        var flags =
            NativeMethods.CREATE_SUSPENDED |
            NativeMethods.CREATE_NO_WINDOW |
            NativeMethods.EXTENDED_STARTUPINFO_PRESENT;

        Assert.True(
            NativeMethods.CreateProcessExW(
                executable,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                true,
                flags,
                IntPtr.Zero,
                null,
                ref startup,
                out var pi),
            $"CreateProcessW failed with Win32 error {Marshal.GetLastWin32Error()}.");

        using var processHandle = new SafeFileHandle(pi.hProcess, ownsHandle: true);
        using var threadHandle = new SafeFileHandle(pi.hThread, ownsHandle: true);
        using var jobHandle = ProcessRunner.CreateKillOnCloseJob();
        Assert.True(NativeMethods.AssignProcessToJobObject(
            jobHandle,
            processHandle.DangerousGetHandle()));

        // The parent must release its copies of the child ends so stdout reaches EOF.
        stdoutWriteHandle.Dispose();
        stderrWriteHandle.Dispose();
        stdinReadHandle.Dispose();
        stdinWriteHandle.Dispose();

        Assert.NotEqual(uint.MaxValue, NativeMethods.ResumeThread(threadHandle));
        Assert.Equal(
            NativeMethods.WAIT_OBJECT_0,
            NativeMethods.WaitForSingleObject(processHandle, 20_000));
        Assert.True(NativeMethods.GetExitCodeProcess(processHandle, out var rawExitCode));

        using var stdoutStream = new FileStream(
            stdoutReadHandle,
            FileAccess.Read,
            4096,
            isAsync: false);
        using var reader = new StreamReader(
            stdoutStream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        var stdoutText = reader.ReadToEnd();

        var unrelatedWasSignaled = unrelated.WaitOne(0);
        Assert.False(
            unrelatedWasSignaled,
            $"Unrelated inheritable event leaked into the child. Exit={unchecked((int)rawExitCode)} stdout={stdoutText}");
        Assert.Equal(0, unchecked((int)rawExitCode));
        Assert.Contains("stdio-ok", stdoutText, StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(
        IntPtr hObject,
        uint dwMask,
        uint dwFlags);
}
