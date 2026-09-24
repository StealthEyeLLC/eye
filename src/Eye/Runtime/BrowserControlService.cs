using System.Text.Json;

namespace StealthEye.Runtime;

public sealed class BrowserControlService(BrowserSessionManager sessions, BrowserTargetStore targets)
{
    public async Task<BrowserNavigateSnapshot> NavigateAsync(
        string targetId,
        string url,
        CancellationToken cancellationToken = default)
    {
        var target = targets.ResolveActive(targetId);
        if (!string.Equals(target.Type, "page", StringComparison.Ordinal))
            throw new ArgumentException($"Target {targetId} is not a page.", nameof(targetId));
        var result = await sessions.NavigateAsync(target.CdpTargetId, url, cancellationToken);
        return new BrowserNavigateSnapshot(
            target.TargetId,
            target.Incarnation,
            result.Url,
            result.FrameId,
            result.LoaderId,
            result.ErrorText);
    }

    public async Task<BrowserEvaluateSnapshot> EvaluateAsync(
        string targetId,
        string expression,
        CancellationToken cancellationToken = default)
    {
        var target = targets.ResolveActive(targetId);
        if (!string.Equals(target.Type, "page", StringComparison.Ordinal))
            throw new ArgumentException($"Target {targetId} is not a page.", nameof(targetId));
        var result = await sessions.EvaluateAsync(target.CdpTargetId, expression, cancellationToken);
        return new BrowserEvaluateSnapshot(
            target.TargetId,
            target.Incarnation,
            result.Type,
            ParseValue(result.ValueJson),
            result.Description,
            result.Threw,
            result.ExceptionText);
    }
    private static JsonElement? ParseValue(string? valueJson)
    {
        if (valueJson is null) return null;
        using var document = JsonDocument.Parse(valueJson);
        return document.RootElement.Clone();
    }}