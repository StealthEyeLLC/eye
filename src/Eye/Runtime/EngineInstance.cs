using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
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
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _rpcGate = new(1, 1);
    private long _nextRequestId;
    private int _disposed;

    private EngineInstance(
        Process process,
        SafeFileHandle jobHandle,
        NamedPipeServerStream pipe,
        StreamReader reader,
        StreamWriter writer,
        string executablePath)
    {
        _process = process;
        _jobHandle = jobHandle;
        _pipe = pipe;
        _reader = reader;
        _writer = writer;
        ExecutablePath = executablePath;
    }
    public string ExecutablePath { get; }
    public EngineHandshake Handshake { get; private set; } = null!;
    public EnginePingResult Ping { get; private set; } = null!;
    public int ProcessId => _process.Id;
    public bool HasExited => _process.HasExited;
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
        StreamReader? reader = null;
        StreamWriter? writer = null;
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

            reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
            var instance = new EngineInstance(process, jobHandle, pipe, reader, writer, executablePath);

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
            reader = null;
            writer = null;
            return instance;
        }
        catch
        {
            if (jobHandle is not null)
            {
                try { ProcessRunner.TerminateJob(jobHandle); } catch { }
            }
            else if (process is { HasExited: false })
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
            reader?.Dispose();
            writer?.Dispose();
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

    public async Task<T> CallAsync<T>(string method, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _rpcGate.WaitAsync(cancellationToken);
        try
        {
            if (_process.HasExited)
                throw new InvalidOperationException($"Eye engine process exited with code {_process.ExitCode}.");

            var id = Interlocked.Increment(ref _nextRequestId);
            var request = new EngineRpcRequest("2.0", id, method);
            await _writer.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), cancellationToken);
            var line = await _reader.ReadLineAsync(cancellationToken)
                ?? throw new EndOfStreamException("Eye engine closed the control pipe.");
            var response = JsonSerializer.Deserialize<EngineRpcResponse<T>>(line)
                ?? throw new InvalidOperationException("Unable to deserialize Eye engine response.");
            if (response.Id != id || response.JsonRpc != "2.0")
                throw new InvalidOperationException("Eye engine response identity mismatch.");
            if (response.Error is not null)
                throw new InvalidOperationException($"Eye engine error {response.Error.Code}: {response.Error.Message}");
            if (response.Result is null)
                throw new InvalidOperationException("Eye engine returned no result.");
            return response.Result;
        }
        finally
        {
            _rpcGate.Release();
        }
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
                    await CallCoreAsync<EngineShutdownResult>(EngineRpcMethods.Shutdown, stopTimeout.Token);
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
            _reader.Dispose();
            _writer.Dispose();
            await _pipe.DisposeAsync();
            _jobHandle.Dispose();
            _process.Dispose();
            _rpcGate.Dispose();
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task<T> CallCoreAsync<T>(string method, CancellationToken cancellationToken)
    {
        await _rpcGate.WaitAsync(cancellationToken);
        try
        {
            var id = Interlocked.Increment(ref _nextRequestId);
            await _writer.WriteLineAsync(JsonSerializer.Serialize(new EngineRpcRequest("2.0", id, method)).AsMemory(), cancellationToken);
            var line = await _reader.ReadLineAsync(cancellationToken)
                ?? throw new EndOfStreamException("Eye engine closed the control pipe.");
            var response = JsonSerializer.Deserialize<EngineRpcResponse<T>>(line)
                ?? throw new InvalidOperationException("Unable to deserialize Eye engine response.");
            if (response.Error is not null)
                throw new InvalidOperationException($"Eye engine error {response.Error.Code}: {response.Error.Message}");
            if (response.Id != id || response.Result is null)
                throw new InvalidOperationException("Eye engine response identity mismatch.");
            return response.Result;
        }
        finally
        {
            _rpcGate.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(EngineInstance));
    }
}
