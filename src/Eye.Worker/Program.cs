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
var pidFile = OptionalArgument(args, "--pid-file");
if (!string.IsNullOrWhiteSpace(pidFile))
{
    var fullPidFile = Path.GetFullPath(pidFile);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPidFile)!);
    File.WriteAllText(fullPidFile, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

await using var controlPipe = new NamedPipeClientStream(".", controlPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
await using var bulkPipe = new NamedPipeClientStream(".", bulkPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
await Task.WhenAll(controlPipe.ConnectAsync(10_000), bulkPipe.ConnectAsync(10_000));

await using var multiplexing = await MultiplexingStream.CreateAsync(bulkPipe);
await using var bulkStreams = await WorkerBulkStreamSet.OfferAsync(multiplexing);
await using var target = new SessionWorkerRpcTarget(bulkStreams.Streams);
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

static string? OptionalArgument(string[] arguments, string name)
{
    for (var i = 0; i < arguments.Length - 1; i++)
    {
        if (string.Equals(arguments[i], name, StringComparison.Ordinal))
            return arguments[i + 1];
    }

    return null;
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

sealed class SessionWorkerRpcTarget(IReadOnlyDictionary<string, Stream> bulkStreams) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _vtWriteGate = new(1, 1);
    private readonly SemaphoreSlim _bulkProbeGate = new(1, 1);
    private ConPtySession? _terminal;
    private BrowserCdpSession? _browser;
    private readonly DesktopUiaWatcher _uiaWatcher = new();

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
    [JsonRpcMethod(WorkerRpcMethods.ArmUiaChange)]
    public WorkerUiaArmResult ArmUiaChange(WorkerUiaWaitRequest request) =>
        _uiaWatcher.Arm(request);
    [JsonRpcMethod(WorkerRpcMethods.WaitUiaChange)]
    public Task<WorkerUiaChangeResult> WaitUiaChangeAsync(CancellationToken cancellationToken) =>
        _uiaWatcher.WaitAsync(cancellationToken);
    [JsonRpcMethod(WorkerRpcMethods.EnsureBrowser)]
    public async Task<WorkerBrowserStatusResult> EnsureBrowserAsync(
        WorkerBrowserEnsureRequest request,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_browser is null || _browser.HasExited)
            {
                if (_browser is not null)
                    await _browser.DisposeAsync();
                _browser = await BrowserCdpSession.StartAsync(request, cancellationToken);
            }
            return _browser.Status();
        }
        finally
        {
            _gate.Release();
        }
    }

    [JsonRpcMethod(WorkerRpcMethods.ObserveBrowserTargets)]
    public Task<WorkerBrowserTargetsResult> ObserveBrowserTargetsAsync(CancellationToken cancellationToken) =>
        RequiredBrowser().ObserveTargetsAsync(cancellationToken);
    [JsonRpcMethod(WorkerRpcMethods.NavigateBrowserTarget)]
    public Task<WorkerBrowserNavigateResult> NavigateBrowserTargetAsync(
        WorkerBrowserNavigateRequest request,
        CancellationToken cancellationToken) =>
        RequiredBrowser().NavigateAsync(request.CdpTargetId, request.Url, cancellationToken);

    [JsonRpcMethod(WorkerRpcMethods.EvaluateBrowserTarget)]
    public Task<WorkerBrowserEvaluateResult> EvaluateBrowserTargetAsync(
        WorkerBrowserEvaluateRequest request,
        CancellationToken cancellationToken) =>
        RequiredBrowser().EvaluateAsync(request.CdpTargetId, request.Expression, cancellationToken);
    [JsonRpcMethod(WorkerRpcMethods.ArmBrowserNavigation)]
    public Task<WorkerBrowserNavigationArmResult> ArmBrowserNavigationAsync(
        WorkerBrowserNavigationArmRequest request,
        CancellationToken cancellationToken) =>
        RequiredBrowser().ArmNavigationAsync(request.CdpTargetId, cancellationToken);

    [JsonRpcMethod(WorkerRpcMethods.WaitBrowserNavigation)]
    public Task<WorkerBrowserNavigationResult> WaitBrowserNavigationAsync(
        CancellationToken cancellationToken) =>
        RequiredBrowser().WaitNavigationAsync(cancellationToken);
    [JsonRpcMethod(WorkerRpcMethods.ObserveBrowserDom)]
    public Task<WorkerBrowserDomResult> ObserveBrowserDomAsync(
        WorkerBrowserDomRequest request,
        CancellationToken cancellationToken) =>
        RequiredBrowser().ObserveDomAsync(
            request.CdpTargetId,
            request.MaxDepth,
            request.MaxNodes,
            cancellationToken);

    [JsonRpcMethod(WorkerRpcMethods.DownloadBrowserTarget)]
    public Task<WorkerBrowserDownloadResult> DownloadBrowserTargetAsync(
        WorkerBrowserDownloadRequest request,
        CancellationToken cancellationToken) =>
        RequiredBrowser().DownloadAsync(
            request.CdpTargetId,
            request.Url,
            request.DownloadDirectory,
            request.TimeoutMs,
            cancellationToken);
    [JsonRpcMethod(WorkerRpcMethods.CaptureWindow)]
    public Task<WorkerWindowCaptureResult> CaptureWindowAsync(
        WorkerWindowCaptureRequest request,
        CancellationToken cancellationToken) =>
        DesktopWgcCapture.CaptureAsync(request, cancellationToken);
    [JsonRpcMethod(WorkerRpcMethods.BulkProbe)]
    public async Task<WorkerBulkProbeResult> BulkProbeAsync(
        WorkerBulkProbeRequest request,
        CancellationToken cancellationToken)
    {
        if (!WorkerBulkChannels.IsKnown(request.Channel))
            throw new ArgumentException($"Unknown bulk channel: {request.Channel}", nameof(request));
        if (request.Length is < 0 or > 1_048_576)
            throw new ArgumentException("Bulk probe length must be between 0 and 1048576.", nameof(request));

        var stream = bulkStreams[request.Channel];
        var gate = string.Equals(request.Channel, WorkerBulkChannels.TerminalVt, StringComparison.Ordinal)
            ? _vtWriteGate
            : _bulkProbeGate;
        await gate.WaitAsync(cancellationToken);
        try
        {
            var bytes = new byte[request.Length];
            if (bytes.Length > 0)
                await stream.ReadExactlyAsync(bytes, cancellationToken);
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            return new WorkerBulkProbeResult(request.Channel, bytes.Length);
        }
        finally
        {
            gate.Release();
        }
    }
    [JsonRpcMethod(WorkerRpcMethods.Shutdown)]
    public WorkerShutdownResult Shutdown() => new(true);

    public async ValueTask DisposeAsync()
    {
        var terminal = Interlocked.Exchange(ref _terminal, null);
        if (terminal is not null)
            await terminal.DisposeAsync();
        var browser = Interlocked.Exchange(ref _browser, null);
        if (browser is not null)
            await browser.DisposeAsync();
        _gate.Dispose();
        _vtWriteGate.Dispose();
        _bulkProbeGate.Dispose();
        _uiaWatcher.Dispose();
    }

    private BrowserCdpSession RequiredBrowser() =>
        _browser ?? throw new InvalidOperationException("No browser is active in this session worker.");

    private ConPtySession RequiredTerminal() =>
        _terminal ?? throw new InvalidOperationException("No terminal is active in this session worker.");

    private async ValueTask WriteVtAsync(ProcessOutputChannel _, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await _vtWriteGate.WaitAsync();
        try
        {
            var vtStream = bulkStreams[WorkerBulkChannels.TerminalVt];
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

sealed class WorkerBulkStreamSet : IAsyncDisposable
{
    private readonly Dictionary<string, MultiplexingStream.Channel> _channels;
    private readonly Dictionary<string, Stream> _streams;

    private WorkerBulkStreamSet(
        Dictionary<string, MultiplexingStream.Channel> channels,
        Dictionary<string, Stream> streams)
    {
        _channels = channels;
        _streams = streams;
    }

    internal IReadOnlyDictionary<string, Stream> Streams => _streams;

    internal static async Task<WorkerBulkStreamSet> OfferAsync(MultiplexingStream multiplexing)
    {
        var channels = new Dictionary<string, MultiplexingStream.Channel>(StringComparer.Ordinal);
        var streams = new Dictionary<string, Stream>(StringComparer.Ordinal);
        try
        {
            foreach (var name in WorkerBulkChannels.All)
            {
                var channel = await multiplexing.OfferChannelAsync(name);
                channels.Add(name, channel);
                streams.Add(name, channel.AsStream());
            }
            return new WorkerBulkStreamSet(channels, streams);
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
