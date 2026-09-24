namespace StealthEye.Runtime;

public sealed class MissionContinuationService(ContextCaptureService context, RelayService relay)
{
    public async Task<MissionContinuationResult> CaptureAndRelayAsync(
        string missionId,
        string source,
        string? note = null,
        bool includeScreenshot = false,
        bool screenshotOcr = false,
        int uiaMaxDepth = 3,
        int uiaMaxNodes = 120,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("source is required.", nameof(source));

        var captured = await context.CaptureAsync(
            missionId,
            includeScreenshot,
            screenshotOcr,
            uiaMaxDepth,
            uiaMaxNodes,
            cancellationToken);
        var message = $"context={captured.ArtifactId}; mission_revision={captured.MissionRevision}";
        if (!string.IsNullOrWhiteSpace(note))
            message += $"; note={note.Trim()}";
        var handoff = relay.Send(missionId, source.Trim(), message);
        return new MissionContinuationResult(captured, handoff);
    }
}