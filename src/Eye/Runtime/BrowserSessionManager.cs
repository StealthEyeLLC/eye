using StealthEye.Contract;

namespace StealthEye.Runtime;

public sealed class BrowserSessionManager(SessionWorkerManager workers) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SessionWorker? _worker;
    private string? _workerExecutablePath;
    private WorkerBrowserStatusResult? _status;

    public async Task<WorkerBrowserStatusResult> EnsureAsync(
        string? chromePath = null,
        string? userDataDir = null,
        string initialUrl = "about:blank",
        bool headless = true,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var worker = await EnsureWorkerAsync(cancellationToken);
            _status = await worker.EnsureBrowserAsync(
                new WorkerBrowserEnsureRequest(chromePath, userDataDir, initialUrl, headless),
                cancellationToken);
            return _status;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WorkerBrowserTargetsResult> ObserveTargetsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var worker = await EnsureWorkerAsync(cancellationToken);
            if (_status is null)
                _status = await worker.EnsureBrowserAsync(new WorkerBrowserEnsureRequest(), cancellationToken);
            return await worker.ObserveBrowserTargetsAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WorkerBrowserNavigateResult> NavigateAsync(
        string cdpTargetId,
        string url,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var worker = await EnsureWorkerAsync(cancellationToken);
            if (_status is null)
                _status = await worker.EnsureBrowserAsync(new WorkerBrowserEnsureRequest(), cancellationToken);
            return await worker.NavigateBrowserTargetAsync(cdpTargetId, url, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WorkerBrowserEvaluateResult> EvaluateAsync(
        string cdpTargetId,
        string expression,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var worker = await EnsureWorkerAsync(cancellationToken);
            if (_status is null)
                _status = await worker.EnsureBrowserAsync(new WorkerBrowserEnsureRequest(), cancellationToken);
            return await worker.EvaluateBrowserTargetAsync(cdpTargetId, expression, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
    public WorkerBrowserStatusResult? Status => _status;

    private async Task<SessionWorker> EnsureWorkerAsync(CancellationToken cancellationToken)
    {
        var desiredPath = workers.ResolveWorkerExecutablePath();
        if (_worker is not null && !_worker.HasExited && string.Equals(_workerExecutablePath, desiredPath, StringComparison.OrdinalIgnoreCase))
            return _worker;

        if (_worker is not null)
            await _worker.DisposeAsync();
        _worker = await workers.StartAsync(cancellationToken);
        _workerExecutablePath = desiredPath;
        _status = null;
        return _worker;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_worker is not null)
                await _worker.DisposeAsync();
            _worker = null;
            _workerExecutablePath = null;
            _status = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}