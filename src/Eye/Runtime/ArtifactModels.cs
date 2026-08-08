using System.Text.Json.Serialization;

namespace StealthEye.Runtime;

public sealed record ArtifactRecord(
    [property: JsonPropertyName("artifact_id")] string ArtifactId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("mime_type")] string? MimeType,
    [property: JsonPropertyName("size_bytes")] long SizeBytes,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("storage_tier")] string StorageTier,
    [property: JsonPropertyName("provenance")] string Provenance,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    string ContentPath);

public sealed record ArtifactPreviewResult(
    string ArtifactId,
    bool TextAvailable,
    string? Text,
    bool Truncated);

public sealed record ArtifactRangeResult(
    string ArtifactId,
    long Offset,
    int BytesRead,
    long NextOffset,
    bool Eof,
    string DataBase64);

public sealed record ArtifactDiffResult(
    string LeftArtifactId,
    string RightArtifactId,
    bool Equal,
    long LeftSizeBytes,
    long RightSizeBytes,
    string LeftSha256,
    string RightSha256,
    long? FirstDifferenceOffset);
