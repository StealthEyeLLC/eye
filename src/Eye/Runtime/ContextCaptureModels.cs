using System.Text.Json.Serialization;

namespace StealthEye.Runtime;

public sealed record ContextCaptureDocument(
    [property: JsonPropertyName("captured_at")] DateTimeOffset CapturedAt,
    [property: JsonPropertyName("mission")] MissionBlackboardRecord Mission,
    [property: JsonPropertyName("desktop")] DesktopWindowSnapshot? Desktop,
    [property: JsonPropertyName("foreground_uia")] UiaQuerySnapshot? ForegroundUia,
    [property: JsonPropertyName("screenshot")] DesktopCaptureSnapshot? Screenshot,
    [property: JsonPropertyName("browser")] BrowserTargetSnapshot? Browser,
    [property: JsonPropertyName("desktop_error")] string? DesktopError,
    [property: JsonPropertyName("uia_error")] string? UiaError,
    [property: JsonPropertyName("capture_error")] string? CaptureError,
    [property: JsonPropertyName("browser_error")] string? BrowserError);

public sealed record ContextCaptureResult(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("mission_revision")] long MissionRevision,
    [property: JsonPropertyName("captured_at")] DateTimeOffset CapturedAt,
    [property: JsonPropertyName("artifact_id")] string ArtifactId,
    [property: JsonPropertyName("artifact_incarnation")] long ArtifactIncarnation,
    [property: JsonPropertyName("size_bytes")] long SizeBytes,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("screenshot_artifact_id")] string? ScreenshotArtifactId = null);