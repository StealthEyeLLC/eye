using System.Diagnostics;
using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class BrowserSessionTests
{
    [Fact]
    public async Task BrowserSession_UsesDedicatedLoopbackCdpAndPersistentWorker()
    {
        var profile = Path.Combine(
            @"C:\Users\StealthEye\AppData\Local\StealthEye\Eye\ChromeTests",
            Guid.NewGuid().ToString("N"));
        var workers = new SessionWorkerManager(WorkerExecutable(), WorkerRpcMethods.CurrentProtocolVersion);
        var browser = new BrowserSessionManager(workers);
        int chromePid = 0;
        try
        {
            var status = await browser.EnsureAsync(
                userDataDir: profile,
                initialUrl: "data:text/html,<title>EyeBrowserTarget</title><h1>eye</h1>");
            chromePid = status.ProcessId;
            Assert.Equal(Path.GetFullPath(profile), status.UserDataDir, ignoreCase: true);
            Assert.StartsWith("Chrome/", status.BrowserVersion, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(status.ProtocolVersion));
            Assert.InRange(status.DebugPort, 1, 65535);
            Assert.False(Process.GetProcessById(chromePid).HasExited);

            WorkerBrowserTargetsResult observed = null!;
            for (var i = 0; i < 30; i++)
            {
                observed = await browser.ObserveTargetsAsync();
                if (observed.Targets.Any(x => x.Type == "page" && x.Url.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase)))
                    break;
                await Task.Delay(100);
            }

            var page = Assert.Single(observed.Targets, x => x.Type == "page");
            Assert.False(string.IsNullOrWhiteSpace(page.CdpTargetId));
            Assert.StartsWith("data:text/html", page.Url, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("EyeBrowserTarget", page.Title, StringComparison.OrdinalIgnoreCase);

            var second = await browser.ObserveTargetsAsync();
            Assert.Equal(status.ProcessId, browser.Status!.ProcessId);
            Assert.Contains(second.Targets, x => x.CdpTargetId == page.CdpTargetId);
        }
        finally
        {
            await browser.DisposeAsync();
            if (chromePid > 0)
            {
                for (var i = 0; i < 40; i++)
                {
                    try
                    {
                        if (Process.GetProcessById(chromePid).HasExited) break;
                    }
                    catch (ArgumentException)
                    {
                        break;
                    }
                    await Task.Delay(100);
                }
                Assert.Throws<ArgumentException>(() => Process.GetProcessById(chromePid));
            }
            if (Directory.Exists(profile)) Directory.Delete(profile, recursive: true);
        }
    }

    private static string WorkerExecutable()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Eye.slnx")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "src", "Eye.Worker", "bin", "Release", "net10.0-windows", "eye-worker.exe");
    }
}