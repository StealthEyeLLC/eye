using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

[Collection("Interactive desktop")]
public sealed class BrowserDispatcherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eye-browser-dispatch-" + Guid.NewGuid().ToString("N"));
    private string? _profile;

    [Fact]
    public async Task Dispatcher_ObservesNavigatesAndEvaluatesStableChromeTarget()
    {
        _profile = Path.Combine(
            @"C:\Users\StealthEye\AppData\Local\StealthEye\Eye\ChromeTests",
            Guid.NewGuid().ToString("N"));
        var workers = new SessionWorkerManager(WorkerExecutable(), WorkerRpcMethods.CurrentProtocolVersion);
        await using var sessions = new BrowserSessionManager(workers);
        await sessions.EnsureAsync(
            userDataDir: _profile,
            initialUrl: "data:text/html,<title>EyeDispatcherBefore</title><main id='content'>before</main>");

        var jobs = new JobStore(Path.Combine(_root, "state"), Path.Combine(_root, "spool", "jobs"));
        var targetStore = new BrowserTargetStore(jobs);
        var observation = new BrowserObservationService(sessions, targetStore);
        var artifacts = new ArtifactStore(jobs);
        var control = new BrowserControlService(sessions, targetStore, new BrowserDomStore(jobs), artifacts);
        var dispatcher = new EyeDispatcher(
            new JobManager(jobs, new ProcessRunner()),
            artifacts,
            browserObservationService: observation,
            browserControlService: control);

        JsonElement observed = default;
        for (var i = 0; i < 30; i++)
        {
            observed = Element(await dispatcher.ExecuteAsync(EyeEffectClass.Inspect, "browser.observe", null));
            Assert.True(observed.GetProperty("ok").GetBoolean(), observed.ToString());
            if (observed.GetProperty("result").GetProperty("targets").EnumerateArray()
                .Any(x => x.GetProperty("type").GetString() == "page" &&
                          x.GetProperty("url").GetString()!.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase)))
                break;
            await Task.Delay(100);
        }

        var targets = observed.GetProperty("result").GetProperty("targets");
        var page = Assert.Single(targets.EnumerateArray(), x => x.GetProperty("type").GetString() == "page");
        var targetId = page.GetProperty("target_id").GetString()!;
        Assert.StartsWith("target_", targetId, StringComparison.Ordinal);
        Assert.False(page.TryGetProperty("cdp_target_id", out _));
        Assert.False(observed.GetProperty("result").TryGetProperty("debug_port", out _));
        Assert.False(observed.GetProperty("result").TryGetProperty("chrome_path", out _));
        Assert.False(observed.GetProperty("result").TryGetProperty("user_data_dir", out _));

        var destination = "data:text/html,<title>EyeDispatcherNavigated</title><main id='content'>dispatcher-eye</main>";
        var navigated = Element(await dispatcher.ExecuteAsync(
            EyeEffectClass.Interact,
            "browser.navigate",
            JsonSerializer.SerializeToElement(new BrowserNavigateArgs(targetId, destination))));
        Assert.True(navigated.GetProperty("ok").GetBoolean(), navigated.ToString());
        Assert.Equal(targetId, navigated.GetProperty("result").GetProperty("target_id").GetString());
        Assert.False(navigated.GetProperty("result").TryGetProperty("cdp_target_id", out _));

        JsonElement evaluated = default;
        for (var i = 0; i < 30; i++)
        {
            evaluated = Element(await dispatcher.ExecuteAsync(
                EyeEffectClass.Interact,
                "browser.evaluate",
                JsonSerializer.SerializeToElement(new BrowserEvaluateArgs(targetId, "document.title"))));
            Assert.True(evaluated.GetProperty("ok").GetBoolean(), evaluated.ToString());
            var result = evaluated.GetProperty("result");
            if (result.TryGetProperty("value", out var value) &&
                value.ValueKind == JsonValueKind.String &&
                value.GetString() == "EyeDispatcherNavigated")
                break;
            await Task.Delay(100);
        }
        Assert.Equal("EyeDispatcherNavigated", evaluated.GetProperty("result").GetProperty("value").GetString());

        var body = Element(await dispatcher.ExecuteAsync(
            EyeEffectClass.Interact,
            "browser.evaluate",
            JsonSerializer.SerializeToElement(new BrowserEvaluateArgs(targetId, "document.querySelector('#content').textContent"))));
        Assert.True(body.GetProperty("ok").GetBoolean(), body.ToString());
        Assert.Equal("dispatcher-eye", body.GetProperty("result").GetProperty("value").GetString());

        var dom = Element(await dispatcher.ExecuteAsync(
            EyeEffectClass.Inspect,
            "browser.dom_snapshot",
            JsonSerializer.SerializeToElement(new BrowserDomArgs(targetId, 6, 1000))));
        Assert.True(dom.GetProperty("ok").GetBoolean(), dom.ToString());
        var domResult = dom.GetProperty("result");
        Assert.Equal(targetId, domResult.GetProperty("target_id").GetString());
        Assert.StartsWith(
            "frame_",
            domResult.GetProperty("frames")[0].GetProperty("frame_id").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            domResult.GetProperty("nodes").EnumerateArray(),
            x => x.GetProperty("node_name").GetString() == "MAIN" &&
                 x.GetProperty("node_id").GetString()!.StartsWith("node_", StringComparison.Ordinal));
        Assert.DoesNotContain("cdp_", domResult.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("backend_node_id", domResult.GetRawText(), StringComparison.OrdinalIgnoreCase);

        var wrongDom = Element(await dispatcher.ExecuteAsync(
            EyeEffectClass.Interact,
            "browser.dom_snapshot",
            JsonSerializer.SerializeToElement(new BrowserDomArgs(targetId, 6, 1000))));
        Assert.False(wrongDom.GetProperty("ok").GetBoolean());
        Assert.Equal("wrong_tool", wrongDom.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("eye_inspect", wrongDom.GetProperty("error").GetProperty("expected").GetProperty("tool").GetString());

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var payload = Encoding.UTF8.GetBytes("dispatcher-download-payload");
        var serve = Task.Run(async () =>
        {
            IOException? last = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var client = await listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                var requestBuffer = new byte[4096];
                _ = await stream.ReadAsync(requestBuffer);
                var headers = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: text/plain\r\n" +
                    "Content-Disposition: attachment; filename=\"dispatcher-download.txt\"\r\n" +
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

        var downloaded = Element(await dispatcher.ExecuteAsync(
            EyeEffectClass.Interact,
            "browser.download",
            JsonSerializer.SerializeToElement(new BrowserDownloadArgs(
                targetId,
                $"http://127.0.0.1:{port}/dispatcher-download.txt",
                15000))));
        await serve.WaitAsync(TimeSpan.FromSeconds(5));
        listener.Stop();

        Assert.True(downloaded.GetProperty("ok").GetBoolean(), downloaded.ToString());
        var downloadResult = downloaded.GetProperty("result");
        var artifactId = downloadResult.GetProperty("artifact_id").GetString()!;
        Assert.StartsWith("artifact_", artifactId, StringComparison.Ordinal);
        Assert.Equal("dispatcher-download.txt", downloadResult.GetProperty("name").GetString());
        Assert.DoesNotContain("path", downloadResult.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cdp", downloadResult.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var preview = await artifacts.PreviewAsync(artifactId, 1024);
        Assert.Equal("dispatcher-download-payload", preview.Text);

        var wrongDownload = Element(await dispatcher.ExecuteAsync(
            EyeEffectClass.Inspect,
            "browser.download",
            JsonSerializer.SerializeToElement(new BrowserDownloadArgs(
                targetId,
                "http://127.0.0.1/",
                1000))));
        Assert.False(wrongDownload.GetProperty("ok").GetBoolean());
        Assert.Equal("wrong_tool", wrongDownload.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("eye_interact", wrongDownload.GetProperty("error").GetProperty("expected").GetProperty("tool").GetString());

        var thrown = Element(await dispatcher.ExecuteAsync(
            EyeEffectClass.Interact,
            "browser.evaluate",
            JsonSerializer.SerializeToElement(new BrowserEvaluateArgs(targetId, "throw new Error('dispatcher-boom')"))));
        Assert.True(thrown.GetProperty("ok").GetBoolean(), thrown.ToString());
        Assert.True(thrown.GetProperty("result").GetProperty("threw").GetBoolean());
        Assert.Contains(
            "dispatcher-boom",
            thrown.GetProperty("result").GetProperty("exception_text").GetString(),
            StringComparison.OrdinalIgnoreCase);

        var wrongNavigate = Element(await dispatcher.ExecuteAsync(
            EyeEffectClass.Inspect,
            "browser.navigate",
            JsonSerializer.SerializeToElement(new BrowserNavigateArgs(targetId, destination))));
        Assert.False(wrongNavigate.GetProperty("ok").GetBoolean());
        Assert.Equal("wrong_tool", wrongNavigate.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("eye_interact", wrongNavigate.GetProperty("error").GetProperty("expected").GetProperty("tool").GetString());

        var wrongObserve = Element(await dispatcher.ExecuteAsync(
            EyeEffectClass.Interact,
            "browser.observe",
            null));
        Assert.False(wrongObserve.GetProperty("ok").GetBoolean());
        Assert.Equal("wrong_tool", wrongObserve.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("eye_inspect", wrongObserve.GetProperty("error").GetProperty("expected").GetProperty("tool").GetString());
    }

    private static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value);

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
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
