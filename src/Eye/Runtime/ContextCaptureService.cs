using System.Text.Json;
using StealthEye.Contract;

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
            desktop is null ? null : ToPublic(desktop),
            foregroundUia is null ? null : ToPublic(foregroundUia),
            screenshot,
            browser is null ? null : ToPublic(browser),
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

    private static UiObserveResult ToPublic(DesktopWindowSnapshot snapshot) => new(
        snapshot.Cursor,
        snapshot.SessionId,
        snapshot.SessionLocked,
        snapshot.SecureDesktop,
        snapshot.InputDesktopAccessible,
        snapshot.InputDesktopName,
        snapshot.ObservedAt,
        snapshot.Windows.Select(window => new UiWindowResult(
            window.WindowId,
            window.Incarnation,
            window.ProcessId,
            window.ProcessName,
            window.Title,
            window.ClassName,
            window.Visible,
            window.Minimized,
            window.Foreground,
            new UiWindowBoundsResult(window.Left, window.Top, window.Right, window.Bottom),
            window.Uia is null ? null : new UiUiaRootResult(
                window.Uia.Name,
                window.Uia.AutomationId,
                window.Uia.ControlType,
                window.Uia.FrameworkId,
                window.Uia.ClassName,
                window.Uia.Enabled,
                window.Uia.Offscreen))).ToArray());

    private static UiQueryResult ToPublic(UiaQuerySnapshot snapshot) => new(
        snapshot.Cursor,
        snapshot.WindowId,
        snapshot.WindowIncarnation,
        snapshot.ObservedAt,
        snapshot.Truncated,
        snapshot.Elements.Select(element => new UiElementResult(
            element.ElementId,
            element.Incarnation,
            element.ParentElementId,
            element.Depth,
            element.Name,
            element.AutomationId,
            element.ControlType,
            element.FrameworkId,
            element.ClassName,
            element.Enabled,
            element.Offscreen,
            element.Focused,
            new UiWindowBoundsResult(
                element.Left,
                element.Top,
                element.Right,
                element.Bottom))).ToArray());

    private static BrowserObserveResult ToPublic(BrowserTargetSnapshot snapshot) => new(
        snapshot.Cursor,
        snapshot.ObservedAt,
        snapshot.BrowserVersion,
        snapshot.ProtocolVersion,
        snapshot.Targets.Select(target => new BrowserTargetResult(
            target.TargetId,
            target.Incarnation,
            target.Type,
            target.Title,
            target.Url)).ToArray());
}
