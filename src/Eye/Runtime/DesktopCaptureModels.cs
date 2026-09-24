using System.Text.Json.Serialization;

namespace StealthEye.Runtime;

public sealed record DesktopCaptureSnapshot(
    [property: JsonPropertyName("window_id")] string WindowId,
    [property: JsonPropertyName("window_incarnation")] long WindowIncarnation,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("dirty_region_count")] int DirtyRegionCount,
    [property: JsonPropertyName("artifact_id")] string ArtifactId,
    [property: JsonPropertyName("artifact_incarnation")] long ArtifactIncarnation,
    [property: JsonPropertyName("size_bytes")] long SizeBytes,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("ocr_text")] string? OcrText = null);