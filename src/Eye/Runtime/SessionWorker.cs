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
    private readonly SessionWorkerBulkStreamSet _bulkStreams;
    private readonly JsonRpc _rpc;
    private readonly InteractiveTaskProcessLease? _interactiveTaskLease;
    private int _disposed;

    private SessionWorker(
        Process process,
        SafeFileHandle jobHandle,
        NamedPipeServerStream controlPipe,
        NamedPipeServerStream bulkPipe,
        MultiplexingStream multiplexing,
        SessionWorkerBulkStreamSet bulkStreams,
        JsonRpc rpc,
        SessionWorkerHandshake handshake,
        string executablePath,
        InteractiveTaskProcessLease? interactiveTaskLease)
    {
        _process = process;
        _jobHandle = jobHandle;
        _controlPipe = controlPipe;
        _bulkPipe = bulkPipe;
        _multiplexing = multiplexing;
        _bulkStreams = bulkStreams;
        _rpc = rpc;
        _interactiveTaskLease = interactiveTaskLease;
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

        var nonce = Guid.NewGuid().ToString("N")[..12];
        var controlName = $"eye-c-{Environment.ProcessId}-{nonce}";
        var bulkName = $"eye-b-{Environment.ProcessId}-{nonce}";
        var activeUserSid = GetActiveUserSid();
        var controlPipe = CreateWorkerPipe(controlName, activeUserSid);
        var bulkPipe = CreateWorkerPipe(bulkName, activeUserSid);

        Process? process = null;
        SafeFileHandle? jobHandle = null;
        MultiplexingStream? multiplexing = null;
        SessionWorkerBulkStreamSet? bulkStreams = null;
        JsonRpc? rpc = null;
        InteractiveTaskProcessLease? interactiveTaskLease = null;
        try
        {
            var launched = LaunchActiveUserWorker(executablePath, controlName, bulkName);
            process = launched.Process;
            jobHandle = launched.JobHandle;
            interactiveTaskLease = launched.Lease;

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
            bulkStreams = await SessionWorkerBulkStreamSet.AcceptAsync(multiplexing, timeout.Token);

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
                bulkStreams,
                rpc,
                handshake,
                executablePath,
                interactiveTaskLease);
            process = null;
            jobHandle = null;
            controlPipe = null!;
            bulkPipe = null!;
            multiplexing = null;
            bulkStreams = null;
            rpc = null;
            interactiveTaskLease = null;
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
            if (bulkStreams is not null) await bulkStreams.DisposeAsync();
            if (multiplexing is not null) await multiplexing.DisposeAsync();
            jobHandle?.Dispose();
            process?.Dispose();
            interactiveTaskLease?.Dispose();
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

    public Task<WorkerDesktopObservationResult> ObserveWindowsAsync(
        bool includeInvisible = false,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkerDesktopObservationResult>(
            WorkerRpcMethods.ObserveWindows,
            new WorkerDesktopObserveRequest(includeInvisible),
            cancellationToken);
    public Task<WorkerUiaQueryResult> QueryUiaAsync(
        long hwnd,
        int maxDepth = 4,
        int maxNodes = 200,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkerUiaQueryResult>(
            WorkerRpcMethods.QueryUia,
            new WorkerUiaQueryRequest(hwnd, maxDepth, maxNodes),
            cancellationToken);
    public Task<WorkerUiaActionResult> ActUiaAsync(
        long hwnd,
        string runtimeId,
        string action,
        string? value = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkerUiaActionResult>(
            WorkerRpcMethods.ActUia,
            new WorkerUiaActionRequest(hwnd, runtimeId, action, value),
            cancellationToken);
    public Task<WorkerUiaArmResult> ArmUiaChangeAsync(
        long hwnd,
        string? runtimeId,
        string[] eventTypes,
        int maxNodes = 5000,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkerUiaArmResult>(
            WorkerRpcMethods.ArmUiaChange,
            new WorkerUiaWaitRequest(hwnd, runtimeId, eventTypes, maxNodes),
            cancellationToken);
    public Task<WorkerUiaChangeResult> WaitUiaChangeAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkerUiaChangeResult>(WorkerRpcMethods.WaitUiaChange, cancellationToken);

    public Task<WorkerBrowserStatusResult> EnsureBrowserAsync(
        WorkerBrowserEnsureRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkerBrowserStatusResult>(WorkerRpcMethods.EnsureBrowser, request, cancellationToken);

    public Task<WorkerBrowserTargetsResult> ObserveBrowserTargetsAsync(
        CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkerBrowserTargetsResult>(WorkerRpcMethods.ObserveBrowserTargets, cancellationToken);

    public Task<WorkerBrowserNavigateResult> NavigateBrowserTargetAsync(
        string cdpTargetId,
        string url,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkerBrowserNavigateResult>(
            WorkerRpcMethods.NavigateBrowserTarget,
            new WorkerBrowserNavigateRequest(cdpTargetId, url),
            cancellationToken);

    public Task<WorkerBrowserEvaluateResult> EvaluateBrowserTargetAsync(
        string cdpTargetId,
        string expression,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkerBrowserEvaluateResult>(
            WorkerRpcMethods.EvaluateBrowserTarget,
            new WorkerBrowserEvaluateRequest(cdpTargetId, expression),
            cancellationToken);
    public Task<WorkerBrowserNavigationArmResult> ArmBrowserNavigationAsync(
        string cdpTargetId,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkerBrowserNavigationArmResult>(
            WorkerRpcMethods.ArmBrowserNavigation,
            new WorkerBrowserNavigationArmRequest(cdpTargetId),
            cancellationToken);

    public Task<WorkerBrowserNavigationResult> WaitBrowserNavigationAsync(
        CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkerBrowserNavigationResult>(
            WorkerRpcMethods.WaitBrowserNavigation,
            cancellationToken);
    public Task<WorkerBrowserDomResult> ObserveBrowserDomAsync(
        string cdpTargetId,
        int maxDepth = 4,
        int maxNodes = 500,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkerBrowserDomResult>(
            WorkerRpcMethods.ObserveBrowserDom,
            new WorkerBrowserDomRequest(cdpTargetId, maxDepth, maxNodes),
            cancellationToken);

    public Task<WorkerBrowserDownloadResult> DownloadBrowserTargetAsync(
        string cdpTargetId,
        string url,
        string downloadDirectory,
        int timeoutMs = 30000,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkerBrowserDownloadResult>(
            WorkerRpcMethods.DownloadBrowserTarget,
            new WorkerBrowserDownloadRequest(cdpTargetId, url, downloadDirectory, timeoutMs),
            cancellationToken);
    public Task<WorkerWindowCaptureResult> CaptureWindowAsync(
        long hwnd,
        string destinationPath,
        int timeoutMs = 5000,
        bool recognizeText = false,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkerWindowCaptureResult>(
            WorkerRpcMethods.CaptureWindow,
            new WorkerWindowCaptureRequest(hwnd, destinationPath, timeoutMs, recognizeText),
            cancellationToken);
    internal async Task<byte[]> ProbeBulkAsync(
        string channel,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!WorkerBulkChannels.IsKnown(channel))
            throw new ArgumentException($"Unknown bulk channel: {channel}", nameof(channel));
        if (payload.Length > 1_048_576)
            throw new ArgumentException("Bulk probe payload may contain at most 1048576 bytes.", nameof(payload));

        var stream = _bulkStreams.Get(channel);
        var rpcTask = InvokeAsync<WorkerBulkProbeResult>(
            WorkerRpcMethods.BulkProbe,
            new WorkerBulkProbeRequest(channel, payload.Length),
            cancellationToken);

        if (!payload.IsEmpty)
            await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var echoed = new byte[payload.Length];
        if (echoed.Length > 0)
            await stream.ReadExactlyAsync(echoed, cancellationToken);

        var result = await rpcTask;
        if (!string.Equals(result.Channel, channel, StringComparison.Ordinal) ||
            result.Length != payload.Length)
            throw new InvalidOperationException("Worker bulk probe response did not match the requested channel/length.");

        return echoed;
    }
    public async Task CopyVtToAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _bulkStreams.Get(WorkerBulkChannels.TerminalVt).CopyToAsync(destination, cancellationToken);
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
            await _bulkStreams.DisposeAsync();
            await _multiplexing.DisposeAsync();
            await _controlPipe.DisposeAsync();
            await _bulkPipe.DisposeAsync();
            _jobHandle.Dispose();
            _process.Dispose();
            _interactiveTaskLease?.Dispose();
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
    private static (Process Process, SafeFileHandle JobHandle, InteractiveTaskProcessLease? Lease) LaunchActiveUserWorker(
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
                    var error = Marshal.GetLastWin32Error();
                    if (error is 5 or 1314)
                    {
                        var fallback = InteractiveTaskProcessLauncher.LaunchDirect(
                            executablePath,
                            arguments,
                            TimeSpan.FromSeconds(10));
                        return (fallback.Process, fallback.JobHandle, fallback.Lease);
                    }

                    throw new InvalidOperationException(
                        $"CreateProcessAsUser(worker) failed with Win32 error {error}.");
                }

                using var processHandle = new SafeFileHandle(pi.hProcess, ownsHandle: true);
                using var threadHandle = new SafeFileHandle(pi.hThread, ownsHandle: true);
                var jobHandle = ProcessRunner.CreateKillOnCloseJob();
                try
                {
                    Win32JobApi.AssignProcess(jobHandle, processHandle.DangerousGetHandle());
                    if (NativeMethods.ResumeThread(threadHandle) == uint.MaxValue)
                        ProcessRunner.ThrowWin32("ResumeThread(worker)");
                    return (Process.GetProcessById((int)pi.dwProcessId), jobHandle, null);
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

internal sealed class SessionWorkerBulkStreamSet : IAsyncDisposable
{
    private readonly Dictionary<string, MultiplexingStream.Channel> _channels;
    private readonly Dictionary<string, Stream> _streams;

    private SessionWorkerBulkStreamSet(
        Dictionary<string, MultiplexingStream.Channel> channels,
        Dictionary<string, Stream> streams)
    {
        _channels = channels;
        _streams = streams;
    }

    internal Stream Get(string name) =>
        _streams.TryGetValue(name, out var stream)
            ? stream
            : throw new ArgumentException($"Unknown bulk channel: {name}", nameof(name));

    internal static async Task<SessionWorkerBulkStreamSet> AcceptAsync(
        MultiplexingStream multiplexing,
        CancellationToken cancellationToken)
    {
        var channels = new Dictionary<string, MultiplexingStream.Channel>(StringComparer.Ordinal);
        var streams = new Dictionary<string, Stream>(StringComparer.Ordinal);
        try
        {
            foreach (var name in WorkerBulkChannels.All)
            {
                var channel = await multiplexing.AcceptChannelAsync(name, cancellationToken);
                channels.Add(name, channel);
                streams.Add(name, channel.AsStream());
            }

            return new SessionWorkerBulkStreamSet(channels, streams);
        }
        catch
        {
            foreach (var stream in streams.Values)
                await stream.DisposeAsync();
            foreach (var channel in channels.Values)
                channel.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var stream in _streams.Values)
            await stream.DisposeAsync();
        foreach (var channel in _channels.Values)
            channel.Dispose();
    }
}