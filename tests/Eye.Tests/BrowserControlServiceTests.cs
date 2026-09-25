using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

[Collection("Interactive desktop")]
public sealed class BrowserControlServiceTests : IDisposable
{
    private readonly string _stateRoot = Path.Combine(Path.GetTempPath(), "eye-browser-control-state-" + Guid.NewGuid().ToString("N"));
    private readonly string _spoolRoot = Path.Combine(Path.GetTempPath(), "eye-browser-control-spool-" + Guid.NewGuid().ToString("N"));
    private string? _profile;

    [Fact]
    public async Task StableTarget_NavigatesAndEvaluatesThroughRawCdp()
    {
        _profile = Path.Combine(
            @"C:\Users\StealthEye\AppData\Local\StealthEye\Eye\ChromeTests",
            Guid.NewGuid().ToString("N"));
        var workers = new SessionWorkerManager(WorkerExecutable(), WorkerRpcMethods.CurrentProtocolVersion);
        await using var sessions = new BrowserSessionManager(workers);
        await sessions.EnsureAsync(
            userDataDir: _profile,
            initialUrl: "data:text/html,<title>Before</title><p>before</p>");
        var jobs = new JobStore(_stateRoot, _spoolRoot);
        var targetStore = new BrowserTargetStore(jobs);
        var observe = new BrowserObservationService(sessions, targetStore);
        var artifacts = new ArtifactStore(jobs);
        var control = new BrowserControlService(
            sessions,
            targetStore,
            new BrowserDomStore(jobs),
            artifacts);

        BrowserTargetSnapshot first = null!;
        for (var i = 0; i < 30; i++)
        {
            first = await observe.ObserveAsync();
            if (first.Targets.Any(x => x.Type == "page" && x.Url.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase)))
                break;
            await Task.Delay(100);
        }
        var page = Assert.Single(first.Targets, x => x.Type == "page");

        var destination = "data:text/html,<title>EyeNavigated</title><main id='content'>hello-eye</main>";
        var navigation = await control.NavigateAsync(page.TargetId, destination);
        Assert.Equal(page.TargetId, navigation.TargetId);
        Assert.Equal(page.Incarnation, navigation.Incarnation);
        Assert.Null(navigation.ErrorText);

        BrowserEvaluateSnapshot title = null!;
        for (var i = 0; i < 30; i++)
        {
            title = await control.EvaluateAsync(page.TargetId, "document.title");
            if (title.Value is { } value && value.ValueKind == System.Text.Json.JsonValueKind.String && value.GetString() == "EyeNavigated")
                break;
            await Task.Delay(100);
        }
        Assert.False(title.Threw, title.ExceptionText);
        Assert.True(title.Value is { ValueKind: JsonValueKind.String }, JsonSerializer.Serialize(title));
        Assert.Equal("EyeNavigated", title.Value!.Value.GetString());

        var body = await control.EvaluateAsync(page.TargetId, "document.querySelector('#content').textContent");
        Assert.False(body.Threw, body.ExceptionText);
        Assert.Equal("hello-eye", body.Value!.Value.GetString());

        var failure = await control.EvaluateAsync(page.TargetId, "throw new Error('eye-boom')");
        Assert.True(failure.Threw);
        Assert.Contains("eye-boom", failure.ExceptionText, StringComparison.OrdinalIgnoreCase);

        // Stable DOM handles survive repeated observation while raw CDP IDs remain private.
        var dom1 = await control.ObserveDomAsync(page.TargetId, maxDepth: 6, maxNodes: 1000);
        var dom2 = await control.ObserveDomAsync(page.TargetId, maxDepth: 6, maxNodes: 1000);
        Assert.True(dom2.Cursor > dom1.Cursor);
        Assert.Equal(page.TargetId, dom1.TargetId);
        Assert.NotEmpty(dom1.Frames);
        Assert.NotEmpty(dom1.Nodes);
        var mainFrame1 = dom1.Frames[0];
        var mainFrame2 = Assert.Single(dom2.Frames, x => x.FrameId == mainFrame1.FrameId);
        Assert.Equal(mainFrame1.Incarnation, mainFrame2.Incarnation);
        Assert.StartsWith("frame_", mainFrame1.FrameId, StringComparison.Ordinal);

        var mainNode1 = Assert.Single(
            dom1.Nodes,
            x => string.Equals(x.NodeName, "MAIN", StringComparison.OrdinalIgnoreCase));
        var mainNode2 = Assert.Single(dom2.Nodes, x => x.NodeId == mainNode1.NodeId);
        Assert.Equal(mainNode1.Incarnation, mainNode2.Incarnation);
        Assert.StartsWith("node_", mainNode1.NodeId, StringComparison.Ordinal);

        // A real Chrome download is promoted to the canonical artifact plane.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var payload = Encoding.UTF8.GetBytes("eye-browser-download-payload");
        var serve = Task.Run(async () =>
        {
            IOException? last = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var client = await listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                var request = new byte[4096];
                _ = await stream.ReadAsync(request);
                var headers = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: text/plain\r\n" +
                    "Content-Disposition: attachment; filename=\"eye-download.txt\"\r\n" +
                    $"Content-Length: {payload.Length}\r\n" +
                    "Connection: close\r\n\r\n");
                try
                {
                    await stream.WriteAsync(headers);
                    await stream.WriteAsync(payload);
                    return;
                }
                catch (IOException ex)
                {
                    last = ex;
                }
            }

            throw new IOException("Chrome did not complete the loopback download request.", last);
        });

        var downloaded = await control.DownloadAsync(
            page.TargetId,
            $"http://127.0.0.1:{port}/eye-download.txt",
            timeoutMs: 15000);
        await serve.WaitAsync(TimeSpan.FromSeconds(5));
        listener.Stop();

        Assert.StartsWith("artifact_", downloaded.ArtifactId, StringComparison.Ordinal);
        Assert.Equal("eye-download.txt", downloaded.Name);
        Assert.Equal(payload.Length, downloaded.SizeBytes);
        Assert.Equal("text/plain", downloaded.MimeType);
        var preview = await artifacts.PreviewAsync(downloaded.ArtifactId, 1024);
        Assert.True(preview.TextAvailable);
        Assert.Equal("eye-browser-download-payload", preview.Text);
        var artifactJson = JsonSerializer.Serialize(downloaded);
        Assert.DoesNotContain("path", artifactJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cdp", artifactJson, StringComparison.OrdinalIgnoreCase);
        BrowserTargetSnapshot second = null!;
        for (var i = 0; i < 30; i++)
        {
            second = await observe.ObserveAsync();
            var current = second.Targets.SingleOrDefault(x => x.TargetId == page.TargetId);
            if (current is not null && current.Title.Contains("EyeNavigated", StringComparison.OrdinalIgnoreCase))
                break;
            await Task.Delay(100);
        }
        var again = Assert.Single(second.Targets, x => x.TargetId == page.TargetId);
        Assert.Equal(page.Incarnation, again.Incarnation);
        Assert.Contains("EyeNavigated", again.Title, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("data:text/html", again.Url, StringComparison.OrdinalIgnoreCase);
    }

    private static string WorkerExecutable()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Eye.slnx")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "src", "Eye.Worker", "bin", "Release", "net10.0-windows", "eye-worker.exe");
    }

    public void Dispose()
    {
        if (_profile is not null)
        {
            for (var attempt = 0; attempt < 30 && Directory.Exists(_profile); attempt++)
            {
                try { Directory.Delete(_profile, recursive: true); }
                catch (IOException) when (attempt < 29) { Thread.Sleep(100); }
                catch (UnauthorizedAccessException) when (attempt < 29) { Thread.Sleep(100); }
            }
        }
        if (Directory.Exists(_stateRoot)) Directory.Delete(_stateRoot, recursive: true);
        if (Directory.Exists(_spoolRoot)) Directory.Delete(_spoolRoot, recursive: true);
    }
}
