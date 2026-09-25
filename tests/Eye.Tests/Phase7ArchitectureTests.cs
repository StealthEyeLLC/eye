namespace Eye.Tests;

public sealed class Phase7ArchitectureTests
{
    [Fact]
    public void DesktopAndBrowserSource_PreservePhase7ArchitectureInvariants()
    {
        var root = RepositoryRoot();
        var workerRoot = Path.Combine(root, "src", "Eye.Worker");
        var runtimeRoot = Path.Combine(root, "src", "Eye", "Runtime");

        var windowInventory = File.ReadAllText(Path.Combine(workerRoot, "DesktopWindowInventory.cs"));
        Assert.Contains("SetProcessDpiAwarenessContext", windowInventory, StringComparison.Ordinal);
        Assert.Contains("PerMonitorV2", windowInventory, StringComparison.Ordinal);

        var wgc = File.ReadAllText(Path.Combine(workerRoot, "DesktopWgcCapture.cs"));
        Assert.Contains("GraphicsCaptureSession.IsSupported", wgc, StringComparison.Ordinal);
        Assert.Contains("DirtyRegionMode", wgc, StringComparison.Ordinal);
        Assert.Contains("OcrEngine", wgc, StringComparison.Ordinal);
        Assert.Contains("request.RecognizeText", wgc, StringComparison.Ordinal);

        var uiaTree = File.ReadAllText(Path.Combine(workerRoot, "DesktopUiaTreeReader.cs"));
        Assert.Contains("CacheRequest", uiaTree, StringComparison.Ordinal);

        var uiaWatcher = File.ReadAllText(Path.Combine(workerRoot, "DesktopUiaWatcher.cs"));
        Assert.Contains("Automation", uiaWatcher, StringComparison.Ordinal);

        var sessionState = File.ReadAllText(Path.Combine(workerRoot, "DesktopSessionState.cs"));
        Assert.Contains("WTSQuerySessionInformationW", sessionState, StringComparison.Ordinal);
        Assert.Contains("OpenInputDesktop", sessionState, StringComparison.Ordinal);

        var browser = File.ReadAllText(Path.Combine(workerRoot, "BrowserCdpSession.cs"));
        Assert.Contains("--remote-debugging-address=127.0.0.1", browser, StringComparison.Ordinal);
        Assert.Contains("--user-data-dir=", browser, StringComparison.Ordinal);
        Assert.Contains("Google", browser, StringComparison.Ordinal);
        Assert.Contains("Chrome", browser, StringComparison.Ordinal);
        Assert.DoesNotContain("playwright", browser, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("node.exe", browser, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("chromium.zip", browser, StringComparison.OrdinalIgnoreCase);

        var domStore = File.ReadAllText(Path.Combine(runtimeRoot, "BrowserDomStore.cs"));
        Assert.Contains("browser_frames", domStore, StringComparison.Ordinal);
        Assert.Contains("browser_nodes", domStore, StringComparison.Ordinal);
        Assert.Contains("observation_cursor", domStore, StringComparison.Ordinal);

        var projectFiles = Directory.GetFiles(
            Path.Combine(root, "src"),
            "*.csproj",
            SearchOption.AllDirectories);
        var projectText = string.Join(
            Environment.NewLine,
            projectFiles.Select(File.ReadAllText));
        Assert.DoesNotContain("Microsoft.Playwright", projectText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Puppeteer", projectText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Selenium", projectText, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Eye.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Unable to locate Eye repository root.");
    }
}