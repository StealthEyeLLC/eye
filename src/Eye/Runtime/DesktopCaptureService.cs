namespace StealthEye.Runtime;

public sealed class DesktopCaptureService
{
    private readonly DesktopWindowStore _windows;
    private readonly SessionWorkerManager _workers;
    private readonly ArtifactStore _artifacts;
    private readonly string _captureRoot;

    public DesktopCaptureService(
        JobStore jobs,
        DesktopWindowStore windows,
        SessionWorkerManager workers,
        ArtifactStore artifacts)
    {
        _windows = windows;
        _workers = workers;
        _artifacts = artifacts;
        var spoolParent = Directory.GetParent(jobs.SpoolRoot)?.FullName ?? jobs.SpoolRoot;
        _captureRoot = Path.Combine(spoolParent, "captures");
        Directory.CreateDirectory(_captureRoot);
    }

    public async Task<DesktopCaptureSnapshot> CaptureAsync(
        string windowId,
        int timeoutMs = 5000,
        bool recognizeText = false,
        CancellationToken cancellationToken = default)
    {
        if (timeoutMs is < 1 or > 60_000)
            throw new ArgumentException("timeout_ms must be between 1 and 60000.", nameof(timeoutMs));

        var window = _windows.ResolveActive(windowId);
        var temporary = Path.Combine(_captureRoot, $"capture_{Guid.NewGuid():N}.png");
        try
        {
            var captured = await _workers.CaptureWindowAsync(
                window.Hwnd,
                temporary,
                timeoutMs,
                recognizeText,
                cancellationToken);
            var artifact = await _artifacts.ImportFileAsync(
                temporary,
                "image",
                "image/png",
                $"{window.WindowId}.png",
                $"wgc:{window.WindowId}:{window.Incarnation}",
                "hot",
                cancellationToken);
            return new DesktopCaptureSnapshot(
                window.WindowId,
                window.Incarnation,
                captured.Width,
                captured.Height,
                captured.DirtyRegionCount,
                artifact.ArtifactId,
                artifact.Incarnation,
                artifact.SizeBytes,
                artifact.Sha256,
                captured.OcrText);
        }
        finally
        {
            try { File.Delete(temporary); } catch { }
        }
    }
}
