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

    private BrowserCdpSession(
        Process chrome,
        HttpClient http,
        string baseUrl,
        string chromePath,
        string userDataDir,
        int debugPort,
        string browserVersion,
        string protocolVersion)
    {
        _chrome = chrome;
        _http = http;
        _baseUrl = baseUrl;
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
        return new WorkerBrowserTargetsResult(
            DateTimeOffset.UtcNow,
            BrowserVersion,
            ProtocolVersion,
            targets.Select(target => new WorkerBrowserTargetInfo(
                target.Id ?? string.Empty,
                target.Type ?? string.Empty,
                target.Title ?? string.Empty,
                target.Url ?? string.Empty)).ToArray());
    }

    public async ValueTask DisposeAsync()
    {
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
        [property: JsonPropertyName("url")] string? Url);
}