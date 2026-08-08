using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace StealthEye.Runtime;

public sealed class ConPtySession : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SafeFileHandle _processHandle;
    private readonly SafeFileHandle _jobHandle;
    private readonly FileStream _input;
    private readonly FileStream _output;
    private readonly Task<string> _outputTask;
    private IntPtr _pseudoConsole;
    private bool _released;
    private bool _inputClosed;
    private bool _disposed;
    private int _columns;
    private int _rows;

    private ConPtySession(
        IntPtr pseudoConsole,
        SafeFileHandle processHandle,
        SafeFileHandle jobHandle,
        FileStream input,
        FileStream output,
        int pid,
        string context,
        string effectiveIdentity,
        int columns,
        int rows,
        ProcessRunHooks? hooks,
        int timeoutMs,
        CancellationToken cancellationToken,
        Stopwatch stopwatch)
    {
        _pseudoConsole = pseudoConsole;
        _processHandle = processHandle;
        _jobHandle = jobHandle;
        _input = input;
        _output = output;
        _columns = columns;
        _rows = rows;
        Pid = pid;
        Context = context;
        EffectiveIdentity = effectiveIdentity;
        _outputTask = PumpOutputAsync(output, hooks);
        Completion = CompleteAsync(hooks, timeoutMs, cancellationToken, stopwatch);
    }

    public int Pid { get; }
    public string Context { get; }
    public string EffectiveIdentity { get; }
    public int Columns { get { lock (_gate) return _columns; } }
    public int Rows { get { lock (_gate) return _rows; } }
    public Task<ProcessRunResult> Completion { get; }

    public static ConPtySession Start(
        RunRequest request,
        int columns,
        int rows,
        ProcessRunHooks? hooks,
        CancellationToken cancellationToken)
    {
        if (columns is < 1 or > short.MaxValue)
            throw new ArgumentException($"columns must be between 1 and {short.MaxValue}.", nameof(columns));
        if (rows is < 1 or > short.MaxValue)
            throw new ArgumentException($"rows must be between 1 and {short.MaxValue}.", nameof(rows));

        var stopwatch = Stopwatch.StartNew();
        var resultContext = request.Context.ToLowerInvariant();
        var launchRequest = resultContext == "wsl" ? ToWslLaunchRequest(request) : request;
        var launchContext = launchRequest.Context.ToLowerInvariant();
        if (launchContext is not ("system" or "user"))
            throw new ArgumentException("ConPTY context must be system, user, or wsl.", nameof(request));

        var sa = new NativeMethods.SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<NativeMethods.SECURITY_ATTRIBUTES>(),
            bInheritHandle = false
        };

        if (!NativeMethods.CreatePipe(out var inputRead, out var inputWrite, ref sa, 0))
            ProcessRunner.ThrowWin32("CreatePipe(ConPTY input)");
        if (!NativeMethods.CreatePipe(out var outputRead, out var outputWrite, ref sa, 0))
        {
            inputRead.Dispose();
            inputWrite.Dispose();
            ProcessRunner.ThrowWin32("CreatePipe(ConPTY output)");
        }

        using (inputRead)
        using (outputWrite)
        {
            var size = new NativeMethods.COORD { X = (short)columns, Y = (short)rows };
            var hr = NativeMethods.CreatePseudoConsole(size, inputRead, outputWrite, 0, out var pseudoConsole);
            if (hr < 0)
            {
                inputWrite.Dispose();
                outputRead.Dispose();
                Marshal.ThrowExceptionForHR(hr);
            }

            IntPtr attributeList = IntPtr.Zero;
            SafeFileHandle? processHandle = null;
            SafeFileHandle? threadHandle = null;
            SafeFileHandle? jobHandle = null;
            SafeAccessTokenHandle? userToken = null;
            IntPtr environment = IntPtr.Zero;

            try
            {
                var startup = CreateStartupInfo(pseudoConsole, ref attributeList);
                var executable = ProcessRunner.ResolveExecutable(launchRequest.FileName);
                var commandLine = new StringBuilder(ProcessRunner.BuildCommandLine(executable ?? launchRequest.FileName, launchRequest.Arguments));
                NativeMethods.PROCESS_INFORMATION pi;
                string identity;

                if (launchContext == "user")
                {
                    var sessionId = ProcessRunner.FindActiveSessionId();
                    if (!NativeMethods.WTSQueryUserToken((uint)sessionId, out userToken))
                        ProcessRunner.ThrowWin32("WTSQueryUserToken");
                    using var userIdentity = new WindowsIdentity(userToken.DangerousGetHandle());
                    identity = userIdentity.Name;
                    if (!NativeMethods.CreateEnvironmentBlock(out environment, userToken, false))
                        ProcessRunner.ThrowWin32("CreateEnvironmentBlock");

                    var workingDirectory = launchRequest.WorkingDirectory;
                    if (string.IsNullOrWhiteSpace(workingDirectory))
                        workingDirectory = ProcessRunner.GetProfileDirectory(userToken);

                    var flags = NativeMethods.CREATE_UNICODE_ENVIRONMENT |
                                NativeMethods.CREATE_SUSPENDED |
                                NativeMethods.EXTENDED_STARTUPINFO_PRESENT;
                    if (!NativeMethods.CreateProcessAsUserExW(
                            userToken,
                            null,
                            commandLine,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            false,
                            flags,
                            environment,
                            workingDirectory,
                            ref startup,
                            out pi))
                    {
                        ProcessRunner.ThrowWin32("CreateProcessAsUser(ConPTY)");
                    }
                }
                else
                {
                    identity = WindowsIdentity.GetCurrent().Name;
                    var flags = NativeMethods.CREATE_SUSPENDED | NativeMethods.EXTENDED_STARTUPINFO_PRESENT;
                    if (!NativeMethods.CreateProcessExW(
                            null,
                            commandLine,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            false,
                            flags,
                            IntPtr.Zero,
                            launchRequest.WorkingDirectory,
                            ref startup,
                            out pi))
                    {
                        ProcessRunner.ThrowWin32("CreateProcess(ConPTY)");
                    }
                }

                processHandle = new SafeFileHandle(pi.hProcess, ownsHandle: true);
                threadHandle = new SafeFileHandle(pi.hThread, ownsHandle: true);
                jobHandle = ProcessRunner.CreateKillOnCloseJob();
                if (!NativeMethods.AssignProcessToJobObject(jobHandle, processHandle.DangerousGetHandle()))
                    ProcessRunner.ThrowWin32("AssignProcessToJobObject(ConPTY)");

                if (NativeMethods.ResumeThread(threadHandle) == uint.MaxValue)
                    ProcessRunner.ThrowWin32("ResumeThread(ConPTY)");

                var inputStream = new FileStream(inputWrite, FileAccess.Write, 4096, isAsync: false);
                var outputStream = new FileStream(outputRead, FileAccess.Read, 4096, isAsync: false);
                inputWrite = null!;
                outputRead = null!;

                var session = new ConPtySession(
                    pseudoConsole,
                    processHandle,
                    jobHandle,
                    inputStream,
                    outputStream,
                    pi.dwProcessId,
                    resultContext,
                    identity,
                    columns,
                    rows,
                    hooks,
                    request.TimeoutMs,
                    cancellationToken,
                    stopwatch);

                processHandle = null;
                jobHandle = null;

                hooks?.Started?.Invoke(pi.dwProcessId, identity);
                return session;
            }
            catch
            {
                if (pseudoConsole != IntPtr.Zero)
                    NativeMethods.ClosePseudoConsole(pseudoConsole);
                inputWrite?.Dispose();
                outputRead?.Dispose();
                processHandle?.Dispose();
                jobHandle?.Dispose();
                throw;
            }
            finally
            {
                threadHandle?.Dispose();
                if (attributeList != IntPtr.Zero)
                {
                    NativeMethods.DeleteProcThreadAttributeList(attributeList);
                    Marshal.FreeHGlobal(attributeList);
                }
                if (environment != IntPtr.Zero)
                    NativeMethods.DestroyEnvironmentBlock(environment);
                userToken?.Dispose();
            }
        }
    }

    public async ValueTask<int> WriteAsync(string text, CancellationToken cancellationToken = default)
    {
        if (text is null)
            throw new ArgumentNullException(nameof(text));
        var bytes = Encoding.UTF8.GetBytes(text);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            await _input.WriteAsync(bytes, cancellationToken);
            await _input.FlushAsync(cancellationToken);
            return bytes.Length;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void Resize(int columns, int rows)
    {
        if (columns is < 1 or > short.MaxValue)
            throw new ArgumentException($"columns must be between 1 and {short.MaxValue}.", nameof(columns));
        if (rows is < 1 or > short.MaxValue)
            throw new ArgumentException($"rows must be between 1 and {short.MaxValue}.", nameof(rows));

        lock (_gate)
        {
            ThrowIfDisposed();
            var hr = NativeMethods.ResizePseudoConsole(
                _pseudoConsole,
                new NativeMethods.COORD { X = (short)columns, Y = (short)rows });
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);
            _columns = columns;
            _rows = rows;
        }
    }

    private static NativeMethods.STARTUPINFOEX_NATIVE CreateStartupInfo(IntPtr pseudoConsole, ref IntPtr attributeList)
    {
        nuint bytes = 0;
        NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref bytes);
        if (bytes == 0)
            ProcessRunner.ThrowWin32("InitializeProcThreadAttributeList(size)");

        attributeList = Marshal.AllocHGlobal(checked((int)bytes));
        if (!NativeMethods.InitializeProcThreadAttributeList(attributeList, 1, 0, ref bytes))
            ProcessRunner.ThrowWin32("InitializeProcThreadAttributeList");
        if (!NativeMethods.UpdateProcThreadAttribute(
                attributeList,
                0,
                NativeMethods.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                pseudoConsole,
                (nuint)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero))
        {
            ProcessRunner.ThrowWin32("UpdateProcThreadAttribute(PSEUDOCONSOLE)");
        }

        return new NativeMethods.STARTUPINFOEX_NATIVE
        {
            StartupInfo = new NativeMethods.STARTUPINFO_NATIVE
            {
                cb = (uint)Marshal.SizeOf<NativeMethods.STARTUPINFOEX_NATIVE>(),
                dwFlags = NativeMethods.STARTF_USESTDHANDLES,
                hStdInput = IntPtr.Zero,
                hStdOutput = IntPtr.Zero,
                hStdError = IntPtr.Zero
            },
            lpAttributeList = attributeList
        };
    }

    private async Task<ProcessRunResult> CompleteAsync(
        ProcessRunHooks? hooks,
        int timeoutMs,
        CancellationToken cancellationToken,
        Stopwatch stopwatch)
    {
        var timedOut = false;
        var cancelled = false;
        try
        {
            var rootWait = await WaitForProcessAsync(_processHandle, timeoutMs, cancellationToken);
            if (rootWait == WaitOutcome.Cancelled)
            {
                cancelled = true;
                ProcessRunner.TerminateJob(_jobHandle);
                NativeMethods.WaitForSingleObject(_processHandle, NativeMethods.INFINITE);
            }
            else if (rootWait == WaitOutcome.TimedOut)
            {
                timedOut = true;
                ProcessRunner.TerminateJob(_jobHandle);
                NativeMethods.WaitForSingleObject(_processHandle, NativeMethods.INFINITE);
            }

            if (!NativeMethods.GetExitCodeProcess(_processHandle, out var rawExitCode))
                ProcessRunner.ThrowWin32("GetExitCodeProcess(ConPTY)");

            ReleasePseudoConsoleAndInput();

            if (!cancelled && !timedOut && !_outputTask.IsCompleted)
            {
                var remaining = timeoutMs > 0 ? timeoutMs - (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds) : Timeout.Infinite;
                if (remaining == 0)
                {
                    timedOut = true;
                    ProcessRunner.TerminateJob(_jobHandle);
                }
                else
                {
                    try
                    {
                        using var drainCts = remaining > 0
                            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                            : null;
                        if (drainCts is not null) drainCts.CancelAfter(remaining);
                        await _outputTask.WaitAsync(drainCts?.Token ?? cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        cancelled = true;
                        ProcessRunner.TerminateJob(_jobHandle);
                    }
                    catch (OperationCanceledException)
                    {
                        timedOut = true;
                        ProcessRunner.TerminateJob(_jobHandle);
                    }
                }
            }

            var output = await _outputTask;
            stopwatch.Stop();
            if (cancelled)
                throw new OperationCanceledException(cancellationToken);

            return new ProcessRunResult(
                Pid,
                unchecked((int)rawExitCode),
                timedOut,
                hooks?.CaptureOutput == false ? string.Empty : output,
                string.Empty,
                Context,
                EffectiveIdentity,
                stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            CloseNative();
        }
    }

    private async Task<string> PumpOutputAsync(Stream output, ProcessRunHooks? hooks)
    {
        using var reader = new StreamReader(output, new UTF8Encoding(false, false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var captured = hooks?.CaptureOutput == false ? null : new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None);
            if (count == 0)
                break;
            ReleasePseudoConsoleOwnership();
            var chunk = new string(buffer, 0, count);
            captured?.Append(chunk);
            if (hooks?.Output is not null)
                await hooks.Output(ProcessOutputChannel.Stdout, chunk);
        }
        return captured?.ToString() ?? string.Empty;
    }

    private void ReleasePseudoConsoleOwnership()
    {
        lock (_gate)
        {
            if (_released || _pseudoConsole == IntPtr.Zero) return;
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100))
            {
                var hr = NativeMethods.ReleasePseudoConsole(_pseudoConsole);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);
                _released = true;
            }
        }
    }

    private void ReleasePseudoConsoleAndInput()
    {
        lock (_gate)
        {
            if (!_inputClosed)
            {
                _inputClosed = true;
                _input.Dispose();
            }
        }
        ReleasePseudoConsoleOwnership();
    }

    private void CloseNative()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            try { _input.Dispose(); } catch { }
            try { _output.Dispose(); } catch { }
            if (_pseudoConsole != IntPtr.Zero)
            {
                NativeMethods.ClosePseudoConsole(_pseudoConsole);
                _pseudoConsole = IntPtr.Zero;
            }
            _processHandle.Dispose();
            _jobHandle.Dispose();
        }
    }

    private void DisposeFailedStart()
    {
        try { ProcessRunner.TerminateJob(_jobHandle); } catch { }
        CloseNative();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed || _pseudoConsole == IntPtr.Zero || _inputClosed)
            throw new InvalidOperationException("The terminal session is no longer active.");
    }

    public async ValueTask DisposeAsync()
    {
        if (!Completion.IsCompleted)
        {
            try { ProcessRunner.TerminateJob(_jobHandle); } catch { }
        }
        try { await Completion; } catch { }
        _writeGate.Dispose();
    }

    private static RunRequest ToWslLaunchRequest(RunRequest request)
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
        return new RunRequest
        {
            Context = "user",
            FileName = "wsl.exe",
            Arguments = [.. arguments],
            TimeoutMs = request.TimeoutMs
        };
    }

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
                _ => throw new InvalidOperationException($"Unexpected terminal wait result: {index}.")
            };
        }, CancellationToken.None);
    }
}
