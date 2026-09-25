using System.Text.Json;

namespace StealthEye.Runtime;

public sealed class BrowserControlService(
    BrowserSessionManager sessions,
    BrowserTargetStore targets,
    BrowserDomStore doms,
    ArtifactStore artifacts)
{
    public async Task<BrowserNavigateSnapshot> NavigateAsync(
        string targetId,
        string url,
        CancellationToken cancellationToken = default)
    {
        var target = RequirePage(targetId);
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
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("expression is required.", nameof(expression));

        var target = RequirePage(targetId);
        var result = await sessions.EvaluateAsync(
            target.CdpTargetId,
            expression,
            cancellationToken);

        return new BrowserEvaluateSnapshot(
            target.TargetId,
            target.Incarnation,
            result.Type,
            ParseValue(result.ValueJson),
            result.Description,
            result.Threw,
            result.ExceptionText);
    }

    public async Task<BrowserDomSnapshot> ObserveDomAsync(
        string targetId,
        int maxDepth = 4,
        int maxNodes = 500,
        CancellationToken cancellationToken = default)
    {
        var target = RequirePage(targetId);
        var observed = await sessions.ObserveDomAsync(
            target.CdpTargetId,
            maxDepth,
            maxNodes,
            cancellationToken);
        return doms.Apply(target, observed);
    }

    public async Task<BrowserDownloadSnapshot> DownloadAsync(
        string targetId,
        string url,
        int timeoutMs = 30000,
        CancellationToken cancellationToken = default)
    {
        var target = RequirePage(targetId);
        var downloaded = await sessions.DownloadAsync(
            target.CdpTargetId,
            url,
            timeoutMs,
            cancellationToken);

        var downloadDirectory = Path.GetDirectoryName(downloaded.Path);
        try
        {
            var artifact = await artifacts.ImportFileAsync(
                downloaded.Path,
                kind: "browser-download",
                mimeType: MimeType(downloaded.SuggestedFilename),
                name: downloaded.SuggestedFilename,
                provenance: $"browser.download:{target.TargetId}:{downloaded.Url}",
                cancellationToken: cancellationToken);

            return new BrowserDownloadSnapshot(
                target.TargetId,
                target.Incarnation,
                downloaded.Url,
                artifact.ArtifactId,
                artifact.Incarnation,
                artifact.Name,
                artifact.SizeBytes,
                artifact.Sha256,
                artifact.MimeType,
                artifact.StorageTier);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(downloadDirectory))
            {
                try { Directory.Delete(downloadDirectory, recursive: true); }
                catch { }
            }
        }
    }

    private BrowserTargetHandle RequirePage(string targetId)
    {
        var target = targets.ResolveActive(targetId);
        if (!string.Equals(target.Type, "page", StringComparison.Ordinal))
            throw new ArgumentException($"Target {targetId} is not a page.", nameof(targetId));
        return target;
    }

    private static JsonElement? ParseValue(string? valueJson)
    {
        if (valueJson is null)
            return null;
        using var document = JsonDocument.Parse(valueJson);
        return document.RootElement.Clone();
    }

    private static string MimeType(string name) =>
        Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".html" or ".htm" => "text/html",
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".zip" => "application/zip",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            _ => "application/octet-stream"
        };
}