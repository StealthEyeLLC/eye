using System.Text.Json;

namespace StealthEye.Runtime;

public sealed class ContextCaptureService
{
    private readonly MissionBlackboardStore _missions;
    private readonly DesktopObservationService _desktop;
    private readonly UiaQueryService _uia;
    private readonly DesktopCaptureService _captures;
    private readonly BrowserObservationService _browser;
    private readonly ArtifactStore _artifacts;
    private readonly string _contextRoot;

    public ContextCaptureService(
        JobStore jobs,
        MissionBlackboardStore missions,
        DesktopObservationService desktop,
        UiaQueryService uia,
        DesktopCaptureService captures,
        BrowserObservationService browser,
        ArtifactStore artifacts)
    {
        _missions = missions;
        _desktop = desktop;
        _uia = uia;
        _captures = captures;
        _browser = browser;
        _artifacts = artifacts;
        var spoolParent = Directory.GetParent(jobs.SpoolRoot)?.FullName ?? jobs.SpoolRoot;
        _contextRoot = Path.Combine(spoolParent, "context");
        Directory.CreateDirectory(_contextRoot);
    }

    public async Task<ContextCaptureResult> CaptureAsync(
        string missionId,
        bool includeScreenshot = false,
        bool screenshotOcr = false,
        int uiaMaxDepth = 3,
        int uiaMaxNodes = 120,
        CancellationToken cancellationToken = default)
    {
        if (uiaMaxDepth is < 0 or > 12)
            throw new ArgumentException("uia_max_depth must be between 0 and 12.", nameof(uiaMaxDepth));
        if (uiaMaxNodes is < 1 or > 2000)
            throw new ArgumentException("uia_max_nodes must be between 1 and 2000.", nameof(uiaMaxNodes));

        var mission = _missions.GetRequired(missionId);
        var capturedAt = DateTimeOffset.UtcNow;
        DesktopWindowSnapshot? desktop = null;
        UiaQuerySnapshot? foregroundUia = null;
        DesktopCaptureSnapshot? screenshot = null;
        BrowserTargetSnapshot? browser = null;
        string? desktopError = null;
        string? uiaError = null;
        string? captureError = null;
        string? browserError = null;

        try
        {
            desktop = await _desktop.ObserveAsync(cancellationToken: cancellationToken);
            var foreground = desktop.Windows.FirstOrDefault(x => x.Foreground);
            if (foreground is not null)
            {
                if (foreground.Uia is not null)
                {
                    try
                    {
                        foregroundUia = await _uia.QueryAsync(
                            foreground.WindowId,
                            uiaMaxDepth,
                            uiaMaxNodes,
                            cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        uiaError = ex.Message;
                    }
                }

                if (includeScreenshot)
                {
                    try
                    {
                        screenshot = await _captures.CaptureAsync(
                            foreground.WindowId,
                            timeoutMs: 10_000,
                            recognizeText: screenshotOcr,
                            cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        captureError = ex.Message;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            desktopError = ex.Message;
        }

        try
        {
            browser = await _browser.TryObserveActiveAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            browserError = ex.Message;
        }

        var document = new ContextCaptureDocument(
            capturedAt,
            mission,
            desktop,
            foregroundUia,
            screenshot,
            browser,
            desktopError,
            uiaError,
            captureError,
            browserError);
        var temporary = Path.Combine(_contextRoot, $"context_{Guid.NewGuid():N}.json");
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(stream, document, cancellationToken: cancellationToken);
            }

            var artifact = await _artifacts.ImportFileAsync(
                temporary,
                "context",
                "application/json",
                $"{mission.MissionId}-context.json",
                $"context.capture:{mission.MissionId}:{mission.Revision}",
                "hot",
                cancellationToken);
            return new ContextCaptureResult(
                mission.MissionId,
                mission.Revision,
                capturedAt,
                artifact.ArtifactId,
                artifact.Incarnation,
                artifact.SizeBytes,
                artifact.Sha256,
                screenshot?.ArtifactId);
        }
        finally
        {
            try { File.Delete(temporary); } catch { }
        }
    }
}