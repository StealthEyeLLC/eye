using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace StealthEye.Runtime;

public sealed class ProcessRunner
{
    public Task<ProcessRunResult> RunAsync(
        RunRequest request,
        CancellationToken cancellationToken = default,
        ProcessRunHooks? hooks = null)
    {
        if (string.IsNullOrWhiteSpace(request.FileName))
            throw new ArgumentException("file_name is required.", nameof(request));

        return request.Context.ToLowerInvariant() switch
        {
            "system" => RunSystemAsync(request, cancellationToken, hooks),
            "user" => RunActiveUserAsync(request, cancellationToken, hooks),
            "wsl" => RunWslAsync(request, cancellationToken, hooks),
            _ => throw new ArgumentException($"Unknown process context: {request.Context}", nameof(request))
        };
    }

    private static async Task<ProcessRunResult> RunSystemAsync(
        RunRequest request,
        CancellationToken cancellationToken,
        ProcessRunHooks? hooks)
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
        using var jobHandle = CreateKillOnCloseJob();
        Win32JobApi.AssignProcess(jobHandle, process.Handle);

        process.StandardInput.Close();
        var identity = WindowsIdentity.GetCurrent().Name;
        hooks?.Started?.Invoke(process.Id, identity);

        var stdoutTask = ReadTextAsync(process.StandardOutput, ProcessOutputChannel.Stdout, hooks);
        var stderrTask = ReadTextAsync(process.StandardError, ProcessOutputChannel.Stderr, hooks);
        var timedOut = false;

        using var timeout = request.TimeoutMs > 0
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        if (timeout is not null)
            timeout.CancelAfter(request.TimeoutMs);
        var waitToken = timeout?.Token ?? cancellationToken;

        try
        {
            await process.WaitForExitAsync(waitToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TerminateJob(jobHandle);
            await process.WaitForExitAsync(CancellationToken.None);
            await DrainOutputAsync(stdoutTask, stderrTask);
            throw;
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            TerminateJob(jobHandle);
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
            identity,
            started.ElapsedMilliseconds);
    }

    private static async Task<ProcessRunResult> RunWslAsync(
        RunRequest request,
        CancellationToken cancellationToken,
        ProcessRunHooks? hooks)
    {
        var arguments = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            arguments.Add("--cd");
            arguments.Add(request.WorkingDirectory);
        }

        arguments.Add("--exec");
        arguments.Add(request.FileName);
        arguments.AddRange(request.Arguments);

        var wslRequest = new RunRequest
        {
            Context = "user",
            FileName = "wsl.exe",
            Arguments = [.. arguments],
            TimeoutMs = request.TimeoutMs
        };

        ProcessRunHooks? wslHooks = hooks is null ? null : new ProcessRunHooks
        {
            CaptureOutput = hooks.CaptureOutput,
            Started = hooks.Started,
            Output = hooks.Output is null
                ? null
                : (channel, text) => hooks.Output(channel, NormalizeWslText(text))
        };

#if EYE_SESSION_WORKER
        var result = await RunActiveUserAsync(wslRequest, cancellationToken, wslHooks, "wsl");
#else
        var started = Stopwatch.StartNew();
        var sessionId = FindActiveSessionId();
        if (!NativeMethods.WTSQueryUserToken((uint)sessionId, out var token))
            ThrowWin32("WTSQueryUserToken(WSL)");

        ProcessRunResult result;
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
        {
            result = await RunActiveUserViaTaskAsync(
                wslRequest,
                cancellationToken,
                wslHooks,
                "wsl",
                identity.Name,
                GetProfileDirectory(token),
                started);
        }
#endif
        return result with
        {
            Stdout = NormalizeWslText(result.Stdout),
            Stderr = NormalizeWslText(result.Stderr)
        };
    }

    private static string NormalizeWslText(string value) =>
        value.Contains('\0') ? value.Replace("\0", string.Empty) : value;

    private static async Task<ProcessRunResult> RunActiveUserAsync(
        RunRequest request,
        CancellationToken cancellationToken,
        ProcessRunHooks? hooks,
        string resultContext = "user")
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

                using var startupAttributes = ProcessStartupAttributeList.CreateStdio(
                    stdinReadHandle,
                    stdoutWriteHandle,
                    stderrWriteHandle,
                    @"winsta0\default");
                var startup = startupAttributes.StartupInfo;

                var executable = ResolveExecutable(request.FileName);
                var commandLine = new StringBuilder(BuildCommandLine(executable ?? request.FileName, request.Arguments));
                var workingDirectory = request.WorkingDirectory;
                if (string.IsNullOrWhiteSpace(workingDirectory))
                    workingDirectory = GetProfileDirectory(token);

                var flags = NativeMethods.CREATE_UNICODE_ENVIRONMENT |
                            NativeMethods.CREATE_SUSPENDED |
                            NativeMethods.CREATE_NO_WINDOW |
                            NativeMethods.EXTENDED_STARTUPINFO_PRESENT;

                if (!NativeMethods.CreateProcessAsUserExW(
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
                    var error = Marshal.GetLastWin32Error();
#if !EYE_SESSION_WORKER
                    if (error is 5 or 1314)
                    {
                        return await RunActiveUserViaTaskAsync(
                            request,
                            cancellationToken,
                            hooks,
                            resultContext,
                            identity.Name,
                            workingDirectory!,
                            started);
                    }

#endif
                    throw new InvalidOperationException(
                        $"CreateProcessAsUser(HANDLE_LIST) failed with Win32 error {error}.");
                }

                using var processHandle = new SafeFileHandle(pi.hProcess, ownsHandle: true);
                using var threadHandle = new SafeFileHandle(pi.hThread, ownsHandle: true);
                using var jobHandle = CreateKillOnCloseJob();

                Win32JobApi.AssignProcess(jobHandle, processHandle.DangerousGetHandle());

                stdoutWriteHandle.Dispose();
                stderrWriteHandle.Dispose();
                stdinReadHandle.Dispose();
                stdinWriteHandle.Dispose();

                var stdoutTask = ReadPipeAsync(stdoutReadHandle, ProcessOutputChannel.Stdout, hooks);
                var stderrTask = ReadPipeAsync(stderrReadHandle, ProcessOutputChannel.Stderr, hooks);

                if (NativeMethods.ResumeThread(threadHandle) == uint.MaxValue)
                    ThrowWin32("ResumeThread");

                hooks?.Started?.Invoke((int)pi.dwProcessId, identity.Name);

                var timedOut = false;
                var waitResult = await WaitForProcessAsync(processHandle, request.TimeoutMs, cancellationToken);
                if (waitResult == WaitOutcome.Cancelled)
                {
                    TerminateJob(jobHandle);
                    NativeMethods.WaitForSingleObject(processHandle, NativeMethods.INFINITE);
                    await DrainOutputAsync(stdoutTask, stderrTask);
                    throw new OperationCanceledException(cancellationToken);
                }

                if (waitResult == WaitOutcome.TimedOut)
                {
                    timedOut = true;
                    TerminateJob(jobHandle);
                    NativeMethods.WaitForSingleObject(processHandle, NativeMethods.INFINITE);
                }

                if (!NativeMethods.GetExitCodeProcess(processHandle, out var rawExitCode))
                    ThrowWin32("GetExitCodeProcess");

                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                started.Stop();

                return new ProcessRunResult(
                    (int)pi.dwProcessId,
                    unchecked((int)rawExitCode),
                    timedOut,
                    stdout,
                    stderr,
                    resultContext,
                    identity.Name,
                    started.ElapsedMilliseconds);
            }
            finally
            {
                NativeMethods.DestroyEnvironmentBlock(environment);
            }
        }
    }

#if !EYE_SESSION_WORKER
    private static async Task<ProcessRunResult> RunActiveUserViaTaskAsync(
        RunRequest request,
        CancellationToken cancellationToken,
        ProcessRunHooks? hooks,
        string resultContext,
        string identity,
        string workingDirectory,
        Stopwatch started)
    {
        var executable = ResolveExecutable(request.FileName);
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            throw new FileNotFoundException(
                $"Unable to resolve active-user executable '{request.FileName}'.",
                request.FileName);

        var outputRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "StealthEye",
            "process-output");
        Directory.CreateDirectory(outputRoot);
        var id = Guid.NewGuid().ToString("N");
        var stdoutPath = Path.Combine(outputRoot, id + ".stdout");
        var stderrPath = Path.Combine(outputRoot, id + ".stderr");

        InteractiveTaskProcessLaunch? launch = null;
        try
        {
            launch = InteractiveTaskProcessLauncher.Launch(
                executable,
                request.Arguments,
                TimeSpan.FromSeconds(10),
                workingDirectory,
                stdoutPath,
                stderrPath);

            hooks?.Started?.Invoke(launch.Process.Id, identity);
            var stdoutTask = ReadGrowingFileAsync(
                stdoutPath,
                launch.Process,
                ProcessOutputChannel.Stdout,
                hooks);
            var stderrTask = ReadGrowingFileAsync(
                stderrPath,
                launch.Process,
                ProcessOutputChannel.Stderr,
                hooks);

            var timedOut = false;
            using var timeout = request.TimeoutMs > 0
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : null;
            if (timeout is not null)
                timeout.CancelAfter(request.TimeoutMs);
            var waitToken = timeout?.Token ?? cancellationToken;

            try
            {
                await launch.Process.WaitForExitAsync(waitToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TerminateJob(launch.JobHandle);
                await launch.Process.WaitForExitAsync(CancellationToken.None);
                await DrainOutputAsync(stdoutTask, stderrTask);
                throw;
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
                TerminateJob(launch.JobHandle);
                await launch.Process.WaitForExitAsync(CancellationToken.None);
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            started.Stop();

            return new ProcessRunResult(
                launch.Process.Id,
                launch.Process.ExitCode,
                timedOut,
                stdout,
                stderr,
                resultContext,
                identity,
                started.ElapsedMilliseconds);
        }
        finally
        {
            if (launch is not null)
            {
                launch.JobHandle.Dispose();
                launch.Process.Dispose();
                launch.Lease.Dispose();
            }

            TryDeleteFile(stdoutPath);
            TryDeleteFile(stderrPath);
        }
    }

    private static async Task<string> ReadGrowingFileAsync(
        string path,
        Process process,
        ProcessOutputChannel channel,
        ProcessRunHooks? hooks)
    {
        while (!File.Exists(path))
        {
            if (process.HasExited)
                break;
            await Task.Delay(20);
        }

        if (!File.Exists(path))
            return string.Empty;

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: true);

        var captured = hooks?.CaptureOutput == false ? null : new StringBuilder();
        var buffer = new char[4096];
        var emptyAfterExit = 0;

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory());
            if (read > 0)
            {
                emptyAfterExit = 0;
                var text = new string(buffer, 0, read);
                captured?.Append(text);
                if (hooks?.Output is not null)
                    await hooks.Output(channel, text);
                continue;
            }

            if (process.HasExited)
            {
                emptyAfterExit++;
                if (emptyAfterExit >= 3)
                    break;
            }

            await Task.Delay(20);
        }

        return captured?.ToString() ?? string.Empty;
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { }
    }

#endif
    private enum WaitOutcome
    {
        Exited,
        TimedOut,
        Cancelled
    }

    private static Task<WaitOutcome> WaitForProcessAsync(
        SafeFileHandle processHandle,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            using var processWait = new EventWaitHandle(false, EventResetMode.ManualReset);
            processWait.SafeWaitHandle = new SafeWaitHandle(processHandle.DangerousGetHandle(), ownsHandle: false);
            var waitMilliseconds = timeoutMs > 0 ? timeoutMs : Timeout.Infinite;

            if (!cancellationToken.CanBeCanceled)
                return WaitHandle.WaitAny([processWait], waitMilliseconds) == WaitHandle.WaitTimeout
                    ? WaitOutcome.TimedOut
                    : WaitOutcome.Exited;

            var index = WaitHandle.WaitAny([processWait, cancellationToken.WaitHandle], waitMilliseconds);
            return index switch
            {
                0 => WaitOutcome.Exited,
                1 => WaitOutcome.Cancelled,
                WaitHandle.WaitTimeout => WaitOutcome.TimedOut,
                _ => throw new InvalidOperationException($"Unexpected process wait result: {index}.")
            };
        }, CancellationToken.None);
    }

    internal static int FindActiveSessionId()
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

    internal static SafeFileHandle CreateKillOnCloseJob() =>
        Win32JobApi.CreateKillOnCloseJob();

    internal static void TerminateJob(SafeFileHandle jobHandle) =>
        Win32JobApi.Terminate(jobHandle);

    private static async Task<string> ReadPipeAsync(
        SafeFileHandle handle,
        ProcessOutputChannel channel,
        ProcessRunHooks? hooks)
    {
        using var stream = new FileStream(handle, FileAccess.Read, 4096, isAsync: false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await ReadTextAsync(reader, channel, hooks);
    }

    private static async Task<string> ReadTextAsync(
        StreamReader reader,
        ProcessOutputChannel channel,
        ProcessRunHooks? hooks)
    {
        var captured = hooks?.CaptureOutput == false ? null : new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None);
            if (count == 0)
                break;

            var chunk = new string(buffer, 0, count);
            captured?.Append(chunk);
            if (hooks?.Output is not null)
                await hooks.Output(channel, chunk);
        }

        return captured?.ToString() ?? string.Empty;
    }

    private static async Task DrainOutputAsync(Task<string> stdoutTask, Task<string> stderrTask)
    {
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask);
        }
        catch
        {
            // Preserve the cancellation path; output failures are secondary once the job is terminated.
        }
    }

    internal static string? ResolveExecutable(string fileName)
    {
        if (fileName.Contains('\\') || fileName.Contains('/'))
            return fileName;

        var buffer = new StringBuilder(32768);
        var length = NativeMethods.SearchPathW(null, fileName, ".exe", buffer.Capacity, buffer, IntPtr.Zero);
        return length > 0 && length < buffer.Capacity ? buffer.ToString() : null;
    }

    internal static string GetProfileDirectory(SafeAccessTokenHandle token)
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

    internal static string BuildCommandLine(string fileName, IEnumerable<string> arguments)
        => string.Join(" ", new[] { QuoteArgument(fileName) }.Concat(arguments.Select(QuoteArgument)));

    internal static string QuoteArgument(string argument)
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

    internal static void ThrowWin32(string operation)
        => throw new InvalidOperationException($"{operation} failed with Win32 error {Marshal.GetLastWin32Error()}.");
}
