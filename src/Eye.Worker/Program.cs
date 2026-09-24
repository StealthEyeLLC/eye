using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Nerdbank.Streams;
using StreamJsonRpc;
using StealthEye.Contract;
using StealthEye.Runtime;
using StealthEye.Worker;

DesktopWindowInventory.EnablePerMonitorV2();

var controlPipeName = RequiredArgument(args, "--control-pipe");
var bulkPipeName = RequiredArgument(args, "--bulk-pipe");

await using var controlPipe = new NamedPipeClientStream(".", controlPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
await using var bulkPipe = new NamedPipeClientStream(".", bulkPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
await Task.WhenAll(controlPipe.ConnectAsync(10_000), bulkPipe.ConnectAsync(10_000));

await using var multiplexing = await MultiplexingStream.CreateAsync(bulkPipe);
using var vtChannel = await multiplexing.OfferChannelAsync("terminal.vt");
await using var vtStream = vtChannel.AsStream();
await using var target = new SessionWorkerRpcTarget(vtStream);
using var rpc = new JsonRpc(EyeRpcTransport.CreateMessageHandler(controlPipe), target);
rpc.StartListening();
try
{
    await rpc.Completion;
}
catch (ConnectionLostException)
{
    // Host owns worker lifetime; pipe closure is the graceful stop signal.
}

static string RequiredArgument(string[] arguments, string name)
{
    for (var i = 0; i < arguments.Length - 1; i++)
    {
        if (string.Equals(arguments[i], name, StringComparison.Ordinal))
            return arguments[i + 1];
    }

    throw new ArgumentException($"Missing required argument: {name}");
}

sealed class SessionWorkerRpcTarget(Stream vtStream) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _vtWriteGate = new(1, 1);
    private ConPtySession? _terminal;

    [JsonRpcMethod(WorkerRpcMethods.Handshake)]
    public SessionWorkerHandshake Handshake() => new(
        WorkerRpcMethods.CurrentProtocolVersion,
        typeof(SessionWorkerRpcTarget).Assembly.GetName().Version?.ToString() ?? "unknown",
        Environment.ProcessId,
        Process.GetCurrentProcess().SessionId);

    [JsonRpcMethod(WorkerRpcMethods.StartTerminal)]
    public async Task<WorkerTerminalStartResult> StartTerminalAsync(
        WorkerTerminalStartRequest request,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_terminal is not null)
                throw new InvalidOperationException("This session worker already owns a terminal.");
            if (request.Context is not ("user" or "wsl"))
                throw new ArgumentException("Session workers accept terminal context user or wsl.");

            var localRequest = request.Context == "wsl"
                ? ToWslRequest(request)
                : new RunRequest
                {
                    Context = "system",
                    FileName = request.FileName,
                    Arguments = request.Arguments,
                    WorkingDirectory = request.WorkingDirectory,
                    TimeoutMs = request.TimeoutMs
                };

            var hooks = new ProcessRunHooks
            {
                CaptureOutput = false,
                Output = WriteVtAsync
            };
            _terminal = ConPtySession.Start(
                localRequest,
                request.Columns,
                request.Rows,
                hooks,
                CancellationToken.None);
            return new WorkerTerminalStartResult(_terminal.Pid, _terminal.EffectiveIdentity);
        }
        finally
        {
            _gate.Release();
        }
    }

    [JsonRpcMethod(WorkerRpcMethods.WriteTerminal)]
    public async Task<WorkerTerminalWriteResult> WriteTerminalAsync(
        WorkerTerminalWriteRequest request,
        CancellationToken cancellationToken)
    {
        var terminal = RequiredTerminal();
        var bytes = await terminal.WriteAsync(request.Text, cancellationToken);
        return new WorkerTerminalWriteResult(bytes);
    }

    [JsonRpcMethod(WorkerRpcMethods.ResizeTerminal)]
    public WorkerTerminalResizeResult ResizeTerminal(WorkerTerminalResizeRequest request)
    {
        var terminal = RequiredTerminal();
        terminal.Resize(request.Columns, request.Rows);
        return new WorkerTerminalResizeResult(terminal.Columns, terminal.Rows);
    }

    [JsonRpcMethod(WorkerRpcMethods.WaitTerminal)]
    public async Task<WorkerTerminalExitResult> WaitTerminalAsync(CancellationToken cancellationToken)
    {
        var terminal = RequiredTerminal();
        var result = await terminal.Completion.WaitAsync(cancellationToken);
        return new WorkerTerminalExitResult(result.ExitCode, result.TimedOut, result.DurationMs);
    }

    [JsonRpcMethod(WorkerRpcMethods.ObserveWindows)]
    public WorkerDesktopObservationResult ObserveWindows(WorkerDesktopObserveRequest request) =>
        DesktopWindowInventory.Observe(request.IncludeInvisible);
    [JsonRpcMethod(WorkerRpcMethods.QueryUia)]
    public WorkerUiaQueryResult QueryUia(WorkerUiaQueryRequest request) =>
        DesktopUiaTreeReader.Query(request);
    [JsonRpcMethod(WorkerRpcMethods.ActUia)]
    public WorkerUiaActionResult ActUia(WorkerUiaActionRequest request) =>
        DesktopUiaActor.Act(request);
    [JsonRpcMethod(WorkerRpcMethods.Shutdown)]
    public WorkerShutdownResult Shutdown() => new(true);

    public async ValueTask DisposeAsync()
    {
        var terminal = Interlocked.Exchange(ref _terminal, null);
        if (terminal is not null)
            await terminal.DisposeAsync();
        _gate.Dispose();
        _vtWriteGate.Dispose();
    }

    private ConPtySession RequiredTerminal() =>
        _terminal ?? throw new InvalidOperationException("No terminal is active in this session worker.");

    private async ValueTask WriteVtAsync(ProcessOutputChannel _, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await _vtWriteGate.WaitAsync();
        try
        {
            await vtStream.WriteAsync(bytes);
            await vtStream.FlushAsync();
        }
        finally
        {
            _vtWriteGate.Release();
        }
    }

    private static RunRequest ToWslRequest(WorkerTerminalStartRequest request)
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
            Context = "system",
            FileName = "wsl.exe",
            Arguments = [.. arguments],
            TimeoutMs = request.TimeoutMs
        };
    }
}
