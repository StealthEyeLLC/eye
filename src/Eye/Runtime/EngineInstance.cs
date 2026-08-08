using System.Diagnostics;
using System.IO.Pipes;
using Microsoft.Win32.SafeHandles;
using StreamJsonRpc;
using StealthEye.Contract;

namespace StealthEye.Runtime;

public sealed record EngineInstanceInfo(
    string EngineVersion,
    int ProcessId,
    string ExecutablePath,
    string[] SupportedOperationIds,
    string WorkerProtocolVersion);

public sealed class EngineInstance : IAsyncDisposable
{
    private readonly Process _process;
    private readonly SafeFileHandle _jobHandle;
    private readonly NamedPipeServerStream _pipe;
    private readonly JsonRpc _rpc;
    private int _disposed;

    private EngineInstance(
        Process process,
        SafeFileHandle jobHandle,
        NamedPipeServerStream pipe,
        JsonRpc rpc,
        string executablePath)
    {
        _process = process;
        _jobHandle = jobHandle;
        _pipe = pipe;
        _rpc = rpc;
        ExecutablePath = executablePath;
    }

    public string ExecutablePath { get; }
    public EngineHandshake Handshake { get; private set; } = null!;
    public EnginePingResult Ping { get; private set; } = null!;
    public int ProcessId => _process.Id;
    public bool HasExited => _process.HasExited;
    public int? ExitCode => _process.HasExited ? _process.ExitCode : null;
    public Task ExitTask => _process.WaitForExitAsync();
    public EngineInstanceInfo Info => new(
        Handshake.EngineVersion,
        ProcessId,
        ExecutablePath,
        Handshake.SupportedOperationIds,
        Handshake.WorkerProtocolVersion);

    public static async Task<EngineInstance> StartAsync(
        string executablePath,
        EyeContractCatalog contract,
        TimeSpan? startupTimeout = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("Engine executable path is required.", nameof(executablePath));
        executablePath = Path.GetFullPath(executablePath);
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("Engine executable not found.", executablePath);

        var pipeName = $"stealtheye-engine-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        Process? process = null;
        SafeFileHandle? jobHandle = null;
        JsonRpc? rpc = null;
        try
        {
            var startInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executablePath)!
            };
            startInfo.ArgumentList.Add("--pipe");
            startInfo.ArgumentList.Add(pipeName);

            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Process.Start returned null for the Eye engine.");
            jobHandle = ProcessRunner.CreateKillOnCloseJob();
            if (!NativeMethods.AssignProcessToJobObject(jobHandle, process.Handle))
                ProcessRunner.ThrowWin32("AssignProcessToJobObject(engine)");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(startupTimeout ?? TimeSpan.FromSeconds(10));

            var connectTask = pipe.WaitForConnectionAsync(timeout.Token);
            var exitTask = process.WaitForExitAsync(CancellationToken.None);
            if (await Task.WhenAny(connectTask, exitTask) == exitTask && !pipe.IsConnected)
                throw new InvalidOperationException($"Eye engine exited before connecting with code {process.ExitCode}.");
            await connectTask;

            rpc = new JsonRpc(EngineRpcTransport.CreateMessageHandler(pipe));
            rpc.StartListening();
            var instance = new EngineInstance(process, jobHandle, pipe, rpc, executablePath);

            var handshake = await instance.CallAsync<EngineHandshake>(EngineRpcMethods.Handshake, timeout.Token);
            var validation = EngineHandshakeValidator.Validate(contract, handshake);
            if (!validation.Compatible)
                throw new InvalidOperationException($"Engine handshake rejected: {validation.ErrorCode}.");
            var ping = await instance.CallAsync<EnginePingResult>(EngineRpcMethods.Ping, timeout.Token);
            if (ping.ProcessId != process.Id || !string.Equals(ping.EngineVersion, handshake.EngineVersion, StringComparison.Ordinal))
                throw new InvalidOperationException("Engine ping identity does not match the handshake/process.");

            instance.SetIdentity(handshake, ping);
            process = null;
            jobHandle = null;
            rpc = null;
            return instance;
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
            jobHandle?.Dispose();
            process?.Dispose();
            await pipe.DisposeAsync();
            throw;
        }
    }

    private void SetIdentity(EngineHandshake handshake, EnginePingResult ping)
    {
        Handshake = handshake;
        Ping = ping;
    }

    public Task<T> CallAsync<T>(string method, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_process.HasExited)
            throw new InvalidOperationException($"Eye engine process exited with code {_process.ExitCode}.");
        return InvokeCoreAsync<T>(method, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            if (!_process.HasExited)
            {
                using var stopTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                stopTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    await InvokeCoreAsync<EngineShutdownResult>(EngineRpcMethods.Shutdown, stopTimeout.Token);
                    _rpc.Dispose();
                    await _pipe.DisposeAsync();
                    await _process.WaitForExitAsync(stopTimeout.Token);
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
            await _pipe.DisposeAsync();
            _jobHandle.Dispose();
            _process.Dispose();
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private Task<T> InvokeCoreAsync<T>(string method, CancellationToken cancellationToken) =>
        _rpc.InvokeWithCancellationAsync<T>(method, Array.Empty<object>(), cancellationToken);

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(EngineInstance));
    }
}
