using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace StealthEye.Runtime;

public sealed class ProcessRunner
{
    public Task<ProcessRunResult> RunAsync(RunRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FileName))
            throw new ArgumentException("file_name is required.", nameof(request));

        return request.Context.ToLowerInvariant() switch
        {
            "system" => RunSystemAsync(request, cancellationToken),
            "user" => RunActiveUserAsync(request, cancellationToken),
            _ => throw new ArgumentException($"Unknown process context: {request.Context}", nameof(request))
        };
    }

    private static async Task<ProcessRunResult> RunSystemAsync(RunRequest request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var psi = new ProcessStartInfo(request.FileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };

        foreach (var argument in request.Arguments)
            psi.ArgumentList.Add(argument);

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            psi.WorkingDirectory = request.WorkingDirectory;

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null.");
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var timedOut = false;

        try
        {
            if (request.TimeoutMs > 0)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(request.TimeoutMs);
                await process.WaitForExitAsync(timeout.Token);
            }
            else
            {
                await process.WaitForExitAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        started.Stop();

        return new ProcessRunResult(
            process.Id,
            process.ExitCode,
            timedOut,
            stdout,
            stderr,
            "system",
            WindowsIdentity.GetCurrent().Name,
            started.ElapsedMilliseconds);
    }

    private static async Task<ProcessRunResult> RunActiveUserAsync(RunRequest request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var sessionId = FindActiveSessionId();

        if (!NativeMethods.WTSQueryUserToken((uint)sessionId, out var token))
            ThrowWin32("WTSQueryUserToken");

        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
        {
            if (!NativeMethods.CreateEnvironmentBlock(out var environment, token, false))
                ThrowWin32("CreateEnvironmentBlock");

            try
            {
                var sa = new NativeMethods.SECURITY_ATTRIBUTES
                {
                    nLength = Marshal.SizeOf<NativeMethods.SECURITY_ATTRIBUTES>(),
                    bInheritHandle = true
                };

                CreatePipePair(ref sa, out var stdoutRead, out var stdoutWrite, parentReads: true);
                CreatePipePair(ref sa, out var stderrRead, out var stderrWrite, parentReads: true);
                CreatePipePair(ref sa, out var stdinRead, out var stdinWrite, parentReads: false);

                using var stdoutReadHandle = stdoutRead;
                using var stdoutWriteHandle = stdoutWrite;
                using var stderrReadHandle = stderrRead;
                using var stderrWriteHandle = stderrWrite;
                using var stdinReadHandle = stdinRead;
                using var stdinWriteHandle = stdinWrite;

                var startup = new NativeMethods.STARTUPINFO
                {
                    cb = Marshal.SizeOf<NativeMethods.STARTUPINFO>(),
                    lpDesktop = "winsta0\\default",
                    dwFlags = NativeMethods.STARTF_USESTDHANDLES,
                    hStdInput = stdinReadHandle.DangerousGetHandle(),
                    hStdOutput = stdoutWriteHandle.DangerousGetHandle(),
                    hStdError = stderrWriteHandle.DangerousGetHandle()
                };

                var executable = ResolveExecutable(request.FileName);
                var commandLine = new StringBuilder(BuildCommandLine(executable ?? request.FileName, request.Arguments));
                var workingDirectory = request.WorkingDirectory;
                if (string.IsNullOrWhiteSpace(workingDirectory))
                    workingDirectory = GetProfileDirectory(token);

                var flags = NativeMethods.CREATE_UNICODE_ENVIRONMENT |
                            NativeMethods.CREATE_SUSPENDED |
                            NativeMethods.CREATE_NO_WINDOW;

                if (!NativeMethods.CreateProcessAsUserW(
                        token,
                        executable,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        true,
                        flags,
                        environment,
                        workingDirectory,
                        ref startup,
                        out var pi))
                {
                    ThrowWin32("CreateProcessAsUser");
                }

                using var processHandle = new SafeFileHandle(pi.hProcess, ownsHandle: true);
                using var threadHandle = new SafeFileHandle(pi.hThread, ownsHandle: true);
                using var jobHandle = CreateKillOnCloseJob();

                if (!NativeMethods.AssignProcessToJobObject(jobHandle, processHandle))
                    ThrowWin32("AssignProcessToJobObject");

                stdoutWriteHandle.Dispose();
                stderrWriteHandle.Dispose();
                stdinReadHandle.Dispose();
                stdinWriteHandle.Dispose();

                var stdoutTask = ReadPipeAsync(stdoutReadHandle, cancellationToken);
                var stderrTask = ReadPipeAsync(stderrReadHandle, cancellationToken);

                if (NativeMethods.ResumeThread(threadHandle) == uint.MaxValue)
                    ThrowWin32("ResumeThread");

                var timedOut = false;
                var waitMs = request.TimeoutMs > 0 ? (uint)request.TimeoutMs : NativeMethods.INFINITE;
                var waitResult = await Task.Run(() => NativeMethods.WaitForSingleObject(processHandle, waitMs), CancellationToken.None);

                if (waitResult == NativeMethods.WAIT_TIMEOUT)
                {
                    timedOut = true;
                    if (!NativeMethods.TerminateJobObject(jobHandle, 1))
                        ThrowWin32("TerminateJobObject");
                    NativeMethods.WaitForSingleObject(processHandle, NativeMethods.INFINITE);
                }
                else if (waitResult != NativeMethods.WAIT_OBJECT_0)
                {
                    ThrowWin32("WaitForSingleObject");
                }

                if (!NativeMethods.GetExitCodeProcess(processHandle, out var rawExitCode))
                    ThrowWin32("GetExitCodeProcess");

                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                started.Stop();

                return new ProcessRunResult(
                    pi.dwProcessId,
                    unchecked((int)rawExitCode),
                    timedOut,
                    stdout,
                    stderr,
                    "user",
                    identity.Name,
                    started.ElapsedMilliseconds);
            }
            finally
            {
                NativeMethods.DestroyEnvironmentBlock(environment);
            }
        }
    }

    private static int FindActiveSessionId()
    {
        if (!NativeMethods.WTSEnumerateSessionsW(IntPtr.Zero, 0, 1, out var buffer, out var count))
            ThrowWin32("WTSEnumerateSessions");

        try
        {
            var size = Marshal.SizeOf<NativeMethods.WTS_SESSION_INFO>();
            for (var i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<NativeMethods.WTS_SESSION_INFO>(buffer + i * size);
                if (item.State == NativeMethods.WTS_CONNECTSTATE_CLASS.WTSActive)
                    return item.SessionId;
            }
        }
        finally
        {
            NativeMethods.WTSFreeMemory(buffer);
        }

        throw new InvalidOperationException("No active interactive Windows session was found.");
    }

    private static void CreatePipePair(
        ref NativeMethods.SECURITY_ATTRIBUTES sa,
        out SafeFileHandle read,
        out SafeFileHandle write,
        bool parentReads)
    {
        if (!NativeMethods.CreatePipe(out read, out write, ref sa, 0))
            ThrowWin32("CreatePipe");

        var parentHandle = parentReads ? read : write;
        if (!NativeMethods.SetHandleInformation(parentHandle, NativeMethods.HANDLE_FLAG_INHERIT, 0))
            ThrowWin32("SetHandleInformation");
    }

    private static SafeFileHandle CreateKillOnCloseJob()
    {
        var raw = NativeMethods.CreateJobObjectW(IntPtr.Zero, null);
        if (raw == IntPtr.Zero)
            ThrowWin32("CreateJobObject");

        var job = new SafeFileHandle(raw, ownsHandle: true);
        var info = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

        if (!NativeMethods.SetInformationJobObject(
                job,
                9,
                ref info,
                (uint)Marshal.SizeOf<NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
        {
            job.Dispose();
            ThrowWin32("SetInformationJobObject");
        }

        return job;
    }

    private static async Task<string> ReadPipeAsync(SafeFileHandle handle, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(handle, FileAccess.Read, 4096, isAsync: false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static string? ResolveExecutable(string fileName)
    {
        if (fileName.Contains('\\') || fileName.Contains('/'))
            return fileName;

        var buffer = new StringBuilder(32768);
        var length = NativeMethods.SearchPathW(null, fileName, ".exe", buffer.Capacity, buffer, IntPtr.Zero);
        return length > 0 && length < buffer.Capacity ? buffer.ToString() : null;
    }

    private static string GetProfileDirectory(SafeAccessTokenHandle token)
    {
        uint size = 0;
        NativeMethods.GetUserProfileDirectoryW(token, null, ref size);
        if (size == 0)
            ThrowWin32("GetUserProfileDirectory(size)");

        var buffer = new StringBuilder((int)size);
        if (!NativeMethods.GetUserProfileDirectoryW(token, buffer, ref size))
            ThrowWin32("GetUserProfileDirectory");

        return buffer.ToString();
    }

    private static string BuildCommandLine(string fileName, IEnumerable<string> arguments)
        => string.Join(" ", new[] { QuoteArgument(fileName) }.Concat(arguments.Select(QuoteArgument)));

    private static string QuoteArgument(string argument)
    {
        if (argument.Length == 0)
            return "\"\"";

        if (!argument.Any(c => char.IsWhiteSpace(c) || c == '"'))
            return argument;

        var result = new StringBuilder("\"");
        var backslashes = 0;

        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                result.Append('\\', backslashes * 2 + 1);
                result.Append('"');
                backslashes = 0;
                continue;
            }

            result.Append('\\', backslashes);
            backslashes = 0;
            result.Append(c);
        }

        result.Append('\\', backslashes * 2);
        result.Append('"');
        return result.ToString();
    }

    private static void ThrowWin32(string operation)
        => throw new InvalidOperationException($"{operation} failed with Win32 error {Marshal.GetLastWin32Error()}.");
}
