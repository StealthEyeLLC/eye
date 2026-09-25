using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using StealthEye.Contract;

namespace StealthEye.Worker;

internal sealed class BrowserCdpSession : IAsyncDisposable
{
    private readonly Process _chrome;
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly Uri _browserSocket;
    private readonly SemaphoreSlim _navigationGate = new(1, 1);
    private BrowserCdpClient? _navigationClient;
    private string? _navigationTargetId;

    private BrowserCdpSession(
        Process chrome,
        HttpClient http,
        string baseUrl,
        Uri browserSocket,
        string chromePath,
        string userDataDir,
        int debugPort,
        string browserVersion,
        string protocolVersion)
    {
        _chrome = chrome;
        _http = http;
        _baseUrl = baseUrl;
        _browserSocket = browserSocket;
        ChromePath = chromePath;
        UserDataDir = userDataDir;
        DebugPort = debugPort;
        BrowserVersion = browserVersion;
        ProtocolVersion = protocolVersion;
    }

    internal string ChromePath { get; }
    internal string UserDataDir { get; }
    internal int DebugPort { get; }
    internal string BrowserVersion { get; }
    internal string ProtocolVersion { get; }
    internal int ProcessId => _chrome.Id;
    internal bool HasExited => _chrome.HasExited;

    internal static async Task<BrowserCdpSession> StartAsync(
        WorkerBrowserEnsureRequest request,
        CancellationToken cancellationToken)
    {
        var chromePath = ResolveChromePath(request.ChromePath);
        var userDataDir = ResolveUserDataDir(request.UserDataDir);
        Directory.CreateDirectory(userDataDir);
        var portFile = Path.Combine(userDataDir, "DevToolsActivePort");
        try { File.Delete(portFile); } catch (FileNotFoundException) { }

        var start = new ProcessStartInfo
        {
            FileName = chromePath,
            WorkingDirectory = Path.GetDirectoryName(chromePath),
            UseShellExecute = false
        };
        start.ArgumentList.Add("--user-data-dir=" + userDataDir);
        start.ArgumentList.Add("--remote-debugging-port=0");
        start.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
        start.ArgumentList.Add("--no-first-run");
        start.ArgumentList.Add("--no-default-browser-check");
        start.ArgumentList.Add("--disable-background-mode");
        start.ArgumentList.Add("--disable-extensions");
        if (request.Headless)
            start.ArgumentList.Add("--headless=new");
        start.ArgumentList.Add(string.IsNullOrWhiteSpace(request.InitialUrl) ? "about:blank" : request.InitialUrl);

        var chrome = Process.Start(start)
            ?? throw new InvalidOperationException("Unable to launch Chrome.");
        HttpClient? http = null;
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            string[]? portLines = null;
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (chrome.HasExited)
                    throw new InvalidOperationException($"Chrome exited before CDP became ready with code {chrome.ExitCode}.");
                try
                {
                    if (File.Exists(portFile))
                    {
                        portLines = await File.ReadAllLinesAsync(portFile, cancellationToken);
                        if (portLines.Length >= 2 && int.TryParse(portLines[0], out _)) break;
                    }
                }
                catch (IOException)
                {
                }
                await Task.Delay(100, cancellationToken);
            }
            if (portLines is null || portLines.Length < 2 || !int.TryParse(portLines[0], out var port))
                throw new TimeoutException("Chrome did not publish DevToolsActivePort within 10 seconds.");

            var baseUrl = $"http://127.0.0.1:{port}";
            http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var version = await http.GetFromJsonAsync<CdpVersion>(baseUrl + "/json/version", cancellationToken)
                ?? throw new InvalidOperationException("Chrome /json/version returned no content.");
            if (string.IsNullOrWhiteSpace(version.WebSocketDebuggerUrl) ||
                !Uri.TryCreate(version.WebSocketDebuggerUrl, UriKind.Absolute, out var debuggerUri) ||
                !debuggerUri.IsLoopback)
                throw new InvalidOperationException("Chrome CDP endpoint is not loopback-bound.");

            return new BrowserCdpSession(
                chrome,
                http,
                baseUrl,
                debuggerUri,
                chromePath,
                userDataDir,
                port,
                version.Browser ?? string.Empty,
                version.ProtocolVersion ?? string.Empty);
        }
        catch
        {
            http?.Dispose();
            if (!chrome.HasExited)
            {
                try { chrome.Kill(entireProcessTree: true); } catch { }
                try { await chrome.WaitForExitAsync(CancellationToken.None); } catch { }
            }
            chrome.Dispose();
            throw;
        }
    }

    internal WorkerBrowserStatusResult Status() => new(
        ChromePath,
        UserDataDir,
        ProcessId,
        DebugPort,
        BrowserVersion,
        ProtocolVersion);

    internal async Task<WorkerBrowserTargetsResult> ObserveTargetsAsync(CancellationToken cancellationToken)
    {
        if (_chrome.HasExited)
            throw new InvalidOperationException($"Chrome exited with code {_chrome.ExitCode}.");
        var targets = await _http.GetFromJsonAsync<CdpTarget[]>(_baseUrl + "/json/list", cancellationToken) ?? [];
        var mapped = new List<WorkerBrowserTargetInfo>(targets.Length);
        foreach (var target in targets)
        {
            var type = target.Type ?? string.Empty;
            var title = target.Title ?? string.Empty;
            if (type == "page" && string.IsNullOrWhiteSpace(title))
                title = await TryResolveDocumentTitleAsync(target, cancellationToken);

            mapped.Add(new WorkerBrowserTargetInfo(
                target.Id ?? string.Empty,
                type,
                title,
                target.Url ?? string.Empty));
        }

        return new WorkerBrowserTargetsResult(
            DateTimeOffset.UtcNow,
            BrowserVersion,
            ProtocolVersion,
            [.. mapped]);
    }

    private static async Task<string> TryResolveDocumentTitleAsync(
        CdpTarget target,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl) ||
            !Uri.TryCreate(target.WebSocketDebuggerUrl, UriKind.Absolute, out var socket) ||
            !socket.IsLoopback)
            return target.Title ?? string.Empty;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var client = await BrowserCdpClient.ConnectAsync(socket, cancellationToken);
                var result = await client.CallAsync(
                    "Runtime.evaluate",
                    new
                    {
                        expression = "document.title",
                        returnByValue = true,
                        awaitPromise = false
                    },
                    cancellationToken);
                var remote = result.GetProperty("result");
                if (remote.TryGetProperty("value", out var value) &&
                    value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var title = value.GetString();
                    if (!string.IsNullOrWhiteSpace(title))
                        return title;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }

            if (attempt < 4)
                await Task.Delay(50, cancellationToken);
        }

        return target.Title ?? string.Empty;
    }

    internal async Task<WorkerBrowserNavigateResult> NavigateAsync(
        string cdpTargetId,
        string url,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cdpTargetId))
            throw new ArgumentException("cdp_target_id is required.", nameof(cdpTargetId));
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("url is required.", nameof(url));
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            throw new ArgumentException("url must be absolute.", nameof(url));

        var socket = await TargetSocketAsync(cdpTargetId, cancellationToken);
        await using var client = await BrowserCdpClient.ConnectAsync(socket, cancellationToken);
        await client.CallAsync(CdpMethods.PageEnable, null, cancellationToken);
        var result = await client.CallAsync("Page.navigate", new { url }, cancellationToken);
        return new WorkerBrowserNavigateResult(
            cdpTargetId,
            url,
            result.TryGetProperty("frameId", out var frameId) ? frameId.GetString() : null,
            result.TryGetProperty("loaderId", out var loaderId) ? loaderId.GetString() : null,
            result.TryGetProperty("errorText", out var errorText) ? errorText.GetString() : null);
    }

    internal async Task<WorkerBrowserNavigationArmResult> ArmNavigationAsync(
        string cdpTargetId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cdpTargetId))
            throw new ArgumentException("cdp_target_id is required.", nameof(cdpTargetId));

        await _navigationGate.WaitAsync(cancellationToken);
        try
        {
            if (_navigationClient is not null)
                throw new InvalidOperationException("A browser navigation watcher is already armed.");

            var socket = await TargetSocketAsync(cdpTargetId, cancellationToken);
            var client = await BrowserCdpClient.ConnectAsync(socket, cancellationToken);
            try
            {
                await client.CallAsync(CdpMethods.PageEnable, null, cancellationToken);
            }
            catch
            {
                await client.DisposeAsync();
                throw;
            }

            _navigationClient = client;
            _navigationTargetId = cdpTargetId;
            return new WorkerBrowserNavigationArmResult(true);
        }
        finally
        {
            _navigationGate.Release();
        }
    }

    internal async Task<WorkerBrowserNavigationResult> WaitNavigationAsync(
        CancellationToken cancellationToken)
    {
        BrowserCdpClient client;
        string targetId;

        await _navigationGate.WaitAsync(cancellationToken);
        try
        {
            client = _navigationClient
                ?? throw new InvalidOperationException("No browser navigation watcher is armed.");
            targetId = _navigationTargetId
                ?? throw new InvalidOperationException("Browser navigation watcher target is missing.");
        }
        finally
        {
            _navigationGate.Release();
        }

        try
        {
            var parameters = await client.WaitForEventAsync("Page.frameNavigated", cancellationToken);
            var frame = parameters.TryGetProperty("frame", out var frameElement)
                ? frameElement
                : default;
            var url = frame.ValueKind == System.Text.Json.JsonValueKind.Object &&
                      frame.TryGetProperty("url", out var urlElement)
                ? urlElement.GetString() ?? string.Empty
                : string.Empty;
            var frameId = frame.ValueKind == System.Text.Json.JsonValueKind.Object &&
                          frame.TryGetProperty("id", out var idElement)
                ? idElement.GetString()
                : null;
            var loaderId = frame.ValueKind == System.Text.Json.JsonValueKind.Object &&
                           frame.TryGetProperty("loaderId", out var loaderElement)
                ? loaderElement.GetString()
                : null;

            return new WorkerBrowserNavigationResult(
                DateTimeOffset.UtcNow,
                targetId,
                url,
                frameId,
                loaderId);
        }
        finally
        {
            await _navigationGate.WaitAsync(CancellationToken.None);
            try
            {
                if (ReferenceEquals(_navigationClient, client))
                {
                    _navigationClient = null;
                    _navigationTargetId = null;
                }
            }
            finally
            {
                _navigationGate.Release();
            }

            await client.DisposeAsync();
        }
    }

    internal async Task<WorkerBrowserDomResult> ObserveDomAsync(
        string cdpTargetId,
        int maxDepth,
        int maxNodes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cdpTargetId))
            throw new ArgumentException("cdp_target_id is required.", nameof(cdpTargetId));
        if (maxDepth is < 1 or > 12)
            throw new ArgumentException("max_depth must be between 1 and 12.", nameof(maxDepth));
        if (maxNodes is < 1 or > 5000)
            throw new ArgumentException("max_nodes must be between 1 and 5000.", nameof(maxNodes));

        var socket = await TargetSocketAsync(cdpTargetId, cancellationToken);
        await using var client = await BrowserCdpClient.ConnectAsync(socket, cancellationToken);
        await client.CallAsync(CdpMethods.PageEnable, null, cancellationToken);

        var framesResult = await client.CallAsync<CdpEmptyRequest, CdpPageGetFrameTreeResult>(
            CdpMethods.PageGetFrameTree,
            new CdpEmptyRequest(),
            cancellationToken);
        var document = await client.CallAsync<CdpDomGetDocumentRequest, CdpDomGetDocumentResult>(
            CdpMethods.DomGetDocument,
            new CdpDomGetDocumentRequest(maxDepth, true),
            cancellationToken);

        var frames = new List<WorkerBrowserFrameInfo>();
        FlattenFrames(framesResult.FrameTree, frames);

        var nodes = new List<WorkerBrowserNodeInfo>(Math.Min(maxNodes, 500));
        var truncated = false;
        FlattenNodes(
            document.Root,
            parentNodeId: null,
            inheritedFrameId: framesResult.FrameTree.Frame.Id,
            nodes,
            maxNodes,
            ref truncated);

        return new WorkerBrowserDomResult(
            DateTimeOffset.UtcNow,
            truncated,
            [.. frames],
            [.. nodes]);
    }

    internal async Task<WorkerBrowserDownloadResult> DownloadAsync(
        string cdpTargetId,
        string url,
        string downloadDirectory,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cdpTargetId))
            throw new ArgumentException("cdp_target_id is required.", nameof(cdpTargetId));
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            throw new ArgumentException("url must be absolute.", nameof(url));
        if (timeoutMs is < 1 or > 300_000)
            throw new ArgumentException("timeout_ms must be between 1 and 300000.", nameof(timeoutMs));

        var root = Path.GetFullPath(downloadDirectory);
        Directory.CreateDirectory(root);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);

        await using var browserClient = await BrowserCdpClient.ConnectAsync(_browserSocket, timeout.Token);
        await browserClient.CallAsync(
            CdpMethods.BrowserSetDownloadBehavior,
            new CdpBrowserSetDownloadBehaviorRequest("allow", root, true),
            timeout.Token);

        var targetSocket = await TargetSocketAsync(cdpTargetId, timeout.Token);
        await using var targetClient = await BrowserCdpClient.ConnectAsync(targetSocket, timeout.Token);
        await targetClient.CallAsync(CdpMethods.PageEnable, null, timeout.Token);
        var navigation = await targetClient.CallAsync<CdpPageNavigateRequest, CdpPageNavigateResult>(
            CdpMethods.PageNavigate,
            new CdpPageNavigateRequest(url),
            timeout.Token);
        if (!string.IsNullOrWhiteSpace(navigation.ErrorText) &&
            !string.Equals(navigation.ErrorText, "net::ERR_ABORTED", StringComparison.Ordinal))
            throw new InvalidOperationException($"Download navigation failed: {navigation.ErrorText}");

        var started = await browserClient.WaitForEventAsync<CdpBrowserDownloadWillBeginEvent>(
            CdpMethods.BrowserDownloadWillBegin,
            timeout.Token);

        CdpBrowserDownloadProgressEvent progress;
        do
        {
            progress = await browserClient.WaitForEventAsync<CdpBrowserDownloadProgressEvent>(
                CdpMethods.BrowserDownloadProgress,
                timeout.Token);
        }
        while (!string.Equals(progress.Guid, started.Guid, StringComparison.Ordinal));

        while (!string.Equals(progress.State, "completed", StringComparison.Ordinal))
        {
            if (string.Equals(progress.State, "canceled", StringComparison.Ordinal))
                throw new InvalidOperationException("Chrome canceled the download.");

            do
            {
                progress = await browserClient.WaitForEventAsync<CdpBrowserDownloadProgressEvent>(
                    CdpMethods.BrowserDownloadProgress,
                    timeout.Token);
            }
            while (!string.Equals(progress.Guid, started.Guid, StringComparison.Ordinal));
        }

        var path = !string.IsNullOrWhiteSpace(progress.FilePath)
            ? Path.GetFullPath(progress.FilePath)
            : Path.GetFullPath(Path.Combine(root, started.SuggestedFilename));
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Chrome reported a download path outside the owned download directory.");

        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (!File.Exists(path) && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(50, timeout.Token);
        if (!File.Exists(path))
            throw new FileNotFoundException("Chrome reported a completed download but the file is missing.", path);

        var length = new FileInfo(path).Length;
        return new WorkerBrowserDownloadResult(
            cdpTargetId,
            started.Url,
            started.Guid,
            started.SuggestedFilename,
            path,
            length);
    }

    private static void FlattenFrames(CdpFrameTree tree, List<WorkerBrowserFrameInfo> frames)
    {
        frames.Add(new WorkerBrowserFrameInfo(
            tree.Frame.Id,
            tree.Frame.ParentId,
            tree.Frame.LoaderId,
            tree.Frame.Url));
        if (tree.ChildFrames is null)
            return;
        foreach (var child in tree.ChildFrames)
            FlattenFrames(child, frames);
    }

    private static void FlattenNodes(
        CdpDomNode node,
        int? parentNodeId,
        string? inheritedFrameId,
        List<WorkerBrowserNodeInfo> nodes,
        int maxNodes,
        ref bool truncated)
    {
        if (nodes.Count >= maxNodes)
        {
            truncated = true;
            return;
        }

        var frameId = node.FrameId ?? inheritedFrameId;
        nodes.Add(new WorkerBrowserNodeInfo(
            node.NodeId,
            node.BackendNodeId,
            parentNodeId,
            frameId,
            node.NodeType,
            node.NodeName,
            node.NodeValue,
            node.Attributes ?? []));

        if (node.Children is null)
            return;
        foreach (var child in node.Children)
        {
            FlattenNodes(child, node.NodeId, frameId, nodes, maxNodes, ref truncated);
            if (truncated)
                break;
        }
    }
    internal async Task<WorkerBrowserEvaluateResult> EvaluateAsync(
        string cdpTargetId,
        string expression,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cdpTargetId))
            throw new ArgumentException("cdp_target_id is required.", nameof(cdpTargetId));
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("expression is required.", nameof(expression));

        var socket = await TargetSocketAsync(cdpTargetId, cancellationToken);
        await using var client = await BrowserCdpClient.ConnectAsync(socket, cancellationToken);
        var result = await client.CallAsync("Runtime.evaluate", new
        {
            expression,
            returnByValue = true,
            awaitPromise = true
        }, cancellationToken);
        var remote = result.GetProperty("result");
        var threw = result.TryGetProperty("exceptionDetails", out var exceptionDetails);
        string? exceptionText = null;
        if (threw)
        {
            exceptionText = exceptionDetails.TryGetProperty("exception", out var exception) &&
                            exception.TryGetProperty("description", out var description)
                ? description.GetString()
                : exceptionDetails.TryGetProperty("text", out var text) ? text.GetString() : "JavaScript evaluation failed.";
        }
        return new WorkerBrowserEvaluateResult(
            cdpTargetId,
            remote.TryGetProperty("type", out var type) ? type.GetString() ?? string.Empty : string.Empty,
            remote.TryGetProperty("value", out var value) ? value.GetRawText() : null,
            remote.TryGetProperty("description", out var remoteDescription) ? remoteDescription.GetString() : null,
            threw,
            exceptionText);
    }

    private async Task<Uri> TargetSocketAsync(string cdpTargetId, CancellationToken cancellationToken)
    {
        var targets = await _http.GetFromJsonAsync<CdpTarget[]>(_baseUrl + "/json/list", cancellationToken) ?? [];
        var target = targets.SingleOrDefault(x => string.Equals(x.Id, cdpTargetId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Unknown CDP target: {cdpTargetId}", nameof(cdpTargetId));
        if (string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl) ||
            !Uri.TryCreate(target.WebSocketDebuggerUrl, UriKind.Absolute, out var socket) ||
            !socket.IsLoopback)
            throw new InvalidOperationException("Target CDP WebSocket endpoint is unavailable or not loopback-bound.");
        return socket;
    }
    public async ValueTask DisposeAsync()
    {
        BrowserCdpClient? navigationClient;
        await _navigationGate.WaitAsync(CancellationToken.None);
        try
        {
            navigationClient = _navigationClient;
            _navigationClient = null;
            _navigationTargetId = null;
        }
        finally
        {
            _navigationGate.Release();
        }
        if (navigationClient is not null)
            await navigationClient.DisposeAsync();
        _navigationGate.Dispose();

        _http.Dispose();
        if (!_chrome.HasExited)
        {
            try { _chrome.Kill(entireProcessTree: true); } catch { }
            try { await _chrome.WaitForExitAsync(CancellationToken.None); } catch { }
        }
        _chrome.Dispose();
    }

    private static string ResolveChromePath(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var full = Path.GetFullPath(requested);
            if (!File.Exists(full)) throw new FileNotFoundException("Chrome executable not found.", full);
            return full;
        }

        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
        ];
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Installed Google Chrome was not found.");
    }

    private static string ResolveUserDataDir(string? requested)
    {
        var path = string.IsNullOrWhiteSpace(requested)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StealthEye", "Eye", "Chrome")
            : requested;
        return Path.GetFullPath(path);
    }

    private sealed record CdpVersion(
        [property: JsonPropertyName("Browser")] string? Browser,
        [property: JsonPropertyName("Protocol-Version")] string? ProtocolVersion,
        [property: JsonPropertyName("webSocketDebuggerUrl")] string? WebSocketDebuggerUrl);

    private sealed record CdpTarget(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("webSocketDebuggerUrl")] string? WebSocketDebuggerUrl);
}
