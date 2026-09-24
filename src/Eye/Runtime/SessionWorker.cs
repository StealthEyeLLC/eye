using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Nerdbank.Streams;
using StreamJsonRpc;
using StealthEye.Contract;

namespace StealthEye.Runtime;

public sealed class SessionWorker : IAsyncDisposable
{
    private readonly Process _process;
    private readonly SafeFileHandle _jobHandle;
    private readonly NamedPipeServerStream _controlPipe;
    private readonly NamedPipeServerStream _bulkPipe;
    private readonly MultiplexingStream _multiplexing;
    private readonly MultiplexingStream.Channel _vtChannel;
    private readonly Stream _vtStream;
    private readonly JsonRpc _rpc;
    private int _disposed;

    private SessionWorker(
        Process process,
        SafeFileHandle jobHandle,
        NamedPipeServerStream controlPipe,
        NamedPipeServerStream bulkPipe,
        MultiplexingStream multiplexing,
        MultiplexingStream.Channel vtChannel,
        Stream vtStream,
        JsonRpc rpc,
        SessionWorkerHandshake handshake,
        string executablePath)
    {
        _process = process;
        _jobHandle = jobHandle;
        _controlPipe = controlPipe;
        _bulkPipe = bulkPipe;
        _multiplexing = multiplexing;
        _vtChannel = vtChannel;
        _vtStream = vtStream;
        _rpc = rpc;
        Handshake = handshake;
        ExecutablePath = executablePath;
    }

    public SessionWorkerHandshake Handshake { get; }
    public string ExecutablePath { get; }
    public int ProcessId => _process.Id;
    public bool HasExited => _process.HasExited;
    public Task ExitTask => _process.WaitForExitAsync();

    public static async Task<SessionWorker> StartAsync(
        string executablePath,
        string expectedWorkerProtocolVersion,
        TimeSpan? startupTimeout = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("Worker executable path is required.", nameof(executablePath));
        executablePath = Path.GetFullPath(executablePath);
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("Session worker executable not found.", executablePath);

        var controlName = $"stealtheye-worker-control-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var bulkName = $"stealtheye-worker-bulk-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var activeUserSid = GetActiveUserSid();
        var controlPipe = CreateWorkerPipe(controlName, activeUserSid);
        var bulkPipe = CreateWorkerPipe(bulkName, activeUserSid);

        Process? process = null;
        SafeFileHandle? jobHandle = null;
        MultiplexingStream? multiplexing = null;
        MultiplexingStream.Channel? vtChannel = null;
        Stream? vtStream = null;
        JsonRpc? rpc = null;
        try
        {
            var launched = LaunchActiveUserWorker(executablePath, controlName, bulkName);
            process = launched.Process;
            jobHandle = launched.JobHandle;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(startupTimeout ?? TimeSpan.FromSeconds(10));
            var controlConnect = controlPipe.WaitForConnectionAsync(timeout.Token);
            var bulkConnect = bulkPipe.WaitForConnectionAsync(timeout.Token);
            var allConnected = Task.WhenAll(controlConnect, bulkConnect);
            var exitTask = process.WaitForExitAsync(CancellationToken.None);
            if (await Task.WhenAny(allConnected, exitTask) == exitTask && (!controlPipe.IsConnected || !bulkPipe.IsConnected))
                throw new InvalidOperationException($"Session worker exited before connecting with code {process.ExitCode}.");
            await allConnected;

            var multiplexTask = MultiplexingStream.CreateAsync(bulkPipe, cancellationToken: timeout.Token);
            rpc = new JsonRpc(EyeRpcTransport.CreateMessageHandler(controlPipe));
            rpc.StartListening();
            multiplexing = await multiplexTask;
            vtChannel = await multiplexing.AcceptChannelAsync("terminal.vt", timeout.Token);
            vtStream = vtChannel.AsStream();

            var handshake = await rpc.InvokeWithCancellationAsync<SessionWorkerHandshake>(
                WorkerRpcMethods.Handshake,
                Array.Empty<object>(),
                timeout.Token);
            if (!string.Equals(handshake.WorkerProtocolVersion, expectedWorkerProtocolVersion, StringComparison.Ordinal))
                throw new InvalidOperationException($"Worker protocol mismatch: expected {expectedWorkerProtocolVersion}, got {handshake.WorkerProtocolVersion}.");
            if (handshake.ProcessId != process.Id)
                throw new InvalidOperationException("Session worker handshake process ID does not match launched process.");
            if (handshake.SessionId != process.SessionId)
                throw new InvalidOperationException("Session worker handshake session ID does not match launched process.");

            var worker = new SessionWorker(
                process,
                jobHandle,
                controlPipe,
                bulkPipe,
                multiplexing,
                vtChannel,
                vtStream,
                rpc,
                handshake,
                executablePath);
            process = null;
            jobHandle = null;
            controlPipe = null!;
            bulkPipe = null!;
            multiplexing = null;
            vtChannel = null;
            vtStream = null;
            rpc = null;
            return worker;
        }
        catch
        {
            rpc?.Dispose();
            if (jobHandle is not null)
            {
                try { ProcessRunner.TerminateJob(jobHandle); } catch { }
            }
            else if (process is { HasExited: false })
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
            if (vtStream is not null) await vtStream.DisposeAsync();
            if (vtChannel is not null) vtChannel.Dispose();
            if (multiplexing is not null) await multiplexing.DisposeAsync();
            jobHandle?.Dispose();
            process?.Dispose();
            await controlPipe.DisposeAsync();
            await bulkPipe.DisposeAsync();
            throw;
        }
    }

    public Task<WorkerTerminalStartResult> StartTerminalAsync(
        WorkerTerminalStartRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkerTerminalStartResult>(WorkerRpcMethods.StartTerminal, request, cancellationToken);

    public Task<WorkerTerminalWriteResult> WriteTerminalAsync(
        string text,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkerTerminalWriteResult>(
            WorkerRpcMethods.WriteTerminal,
            new WorkerTerminalWriteRequest(text),
            cancellationToken);

    public Task<WorkerTerminalResizeResult> ResizeTerminalAsync(
        int columns,
        int rows,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkerTerminalResizeResult>(
            WorkerRpcMethods.ResizeTerminal,
            new WorkerTerminalResizeRequest(columns, rows),
            cancellationToken);

    public Task<WorkerTerminalExitResult> WaitTerminalAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkerTerminalExitResult>(WorkerRpcMethods.WaitTerminal, cancellationToken);

    public async Task CopyVtToAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _vtStream.CopyToAsync(destination, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            if (!_process.HasExited)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    await _rpc.InvokeWithCancellationAsync<WorkerShutdownResult>(
                        WorkerRpcMethods.Shutdown,
                        Array.Empty<object>(),
                        timeout.Token);
                    _rpc.Dispose();
                    await _controlPipe.DisposeAsync();
                    await _process.WaitForExitAsync(timeout.Token);
                }
                catch
                {
                    ProcessRunner.TerminateJob(_jobHandle);
                    await _process.WaitForExitAsync(CancellationToken.None);
                }
            }
        }
        finally
        {
            _rpc.Dispose();
            await _vtStream.DisposeAsync();
            _vtChannel.Dispose();
            await _multiplexing.DisposeAsync();
            await _controlPipe.DisposeAsync();
            await _bulkPipe.DisposeAsync();
            _jobHandle.Dispose();
            _process.Dispose();
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private Task<T> InvokeAsync<T>(string method, CancellationToken cancellationToken) =>
        _rpc.InvokeWithCancellationAsync<T>(method, Array.Empty<object>(), cancellationToken);

    private Task<T> InvokeAsync<T>(string method, object argument, CancellationToken cancellationToken) =>
        _rpc.InvokeWithCancellationAsync<T>(method, [argument], cancellationToken);

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(SessionWorker));
        if (_process.HasExited)
            throw new InvalidOperationException($"Session worker exited with code {_process.ExitCode}.");
    }

    private static SecurityIdentifier GetActiveUserSid()
    {
        var sessionId = ProcessRunner.FindActiveSessionId();
        if (!NativeMethods.WTSQueryUserToken((uint)sessionId, out var token))
            ProcessRunner.ThrowWin32("WTSQueryUserToken(worker pipe)");
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            return identity.User ?? throw new InvalidOperationException("Active-user token has no SID.");
    }

    private static NamedPipeServerStream CreateWorkerPipe(string name, SecurityIdentifier activeUserSid)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            activeUserSid,
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            name,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            security,
            HandleInheritability.None,
            (PipeAccessRights)0);
    }
    private static (Process Process, SafeFileHandle JobHandle) LaunchActiveUserWorker(
        string executablePath,
        string controlPipeName,
        string bulkPipeName)
    {
        var sessionId = ProcessRunner.FindActiveSessionId();
        if (!NativeMethods.WTSQueryUserToken((uint)sessionId, out var token))
            ProcessRunner.ThrowWin32("WTSQueryUserToken(worker)");

        using (token)
        {
            if (!NativeMethods.CreateEnvironmentBlock(out var environment, token, false))
                ProcessRunner.ThrowWin32("CreateEnvironmentBlock(worker)");

            try
            {
                var startup = new NativeMethods.STARTUPINFO
                {
                    cb = Marshal.SizeOf<NativeMethods.STARTUPINFO>(),
                    lpDesktop = "winsta0\\default"
                };
                var arguments = new[] { "--control-pipe", controlPipeName, "--bulk-pipe", bulkPipeName };
                var commandLine = new StringBuilder(ProcessRunner.BuildCommandLine(executablePath, arguments));
                var flags = NativeMethods.CREATE_UNICODE_ENVIRONMENT |
                            NativeMethods.CREATE_SUSPENDED |
                            NativeMethods.CREATE_NO_WINDOW;
                if (!NativeMethods.CreateProcessAsUserW(
                        token,
                        executablePath,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        flags,
                        environment,
                        Path.GetDirectoryName(executablePath),
                        ref startup,
                        out var pi))
                {
                    ProcessRunner.ThrowWin32("CreateProcessAsUser(worker)");
                }

                using var processHandle = new SafeFileHandle(pi.hProcess, ownsHandle: true);
                using var threadHandle = new SafeFileHandle(pi.hThread, ownsHandle: true);
                var jobHandle = ProcessRunner.CreateKillOnCloseJob();
                try
                {
                    if (!NativeMethods.AssignProcessToJobObject(jobHandle, processHandle.DangerousGetHandle()))
                        ProcessRunner.ThrowWin32("AssignProcessToJobObject(worker)");
                    if (NativeMethods.ResumeThread(threadHandle) == uint.MaxValue)
                        ProcessRunner.ThrowWin32("ResumeThread(worker)");
                    return (Process.GetProcessById((int)pi.dwProcessId), jobHandle);
                }
                catch
                {
                    jobHandle.Dispose();
                    throw;
                }
            }
            finally
            {
                NativeMethods.DestroyEnvironmentBlock(environment);
            }
        }
    }
}
