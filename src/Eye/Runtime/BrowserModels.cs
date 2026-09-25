using System.Text.Json;
using System.Text.Json.Serialization;

namespace StealthEye.Runtime;

public sealed record BrowserTargetState(
    [property: JsonPropertyName("target_id")] string TargetId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("url")] string Url);

public sealed record BrowserTargetSnapshot(
    [property: JsonPropertyName("cursor")] long Cursor,
    [property: JsonPropertyName("observed_at")] DateTimeOffset ObservedAt,
    [property: JsonPropertyName("browser_version")] string BrowserVersion,
    [property: JsonPropertyName("protocol_version")] string ProtocolVersion,
    [property: JsonPropertyName("targets")] BrowserTargetState[] Targets);

public sealed record BrowserTargetHandle(
    string TargetId,
    long Incarnation,
    string CdpTargetId,
    string Type,
    string Url);
public sealed record BrowserNavigateSnapshot(
    [property: JsonPropertyName("target_id")] string TargetId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("frame_id")] string? FrameId,
    [property: JsonPropertyName("loader_id")] string? LoaderId,
    [property: JsonPropertyName("error_text")] string? ErrorText);

public sealed record BrowserEvaluateSnapshot(
    [property: JsonPropertyName("target_id")] string TargetId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value")] JsonElement? Value,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("threw")] bool Threw,
    [property: JsonPropertyName("exception_text")] string? ExceptionText);

public sealed record BrowserFrameState(
    [property: JsonPropertyName("frame_id")] string FrameId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("parent_frame_id")] string? ParentFrameId,
    [property: JsonPropertyName("loader_id")] string? LoaderId,
    [property: JsonPropertyName("url")] string Url);

public sealed record BrowserNodeState(
    [property: JsonPropertyName("node_id")] string NodeId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("parent_node_id")] string? ParentNodeId,
    [property: JsonPropertyName("frame_id")] string? FrameId,
    [property: JsonPropertyName("node_type")] int NodeType,
    [property: JsonPropertyName("node_name")] string NodeName,
    [property: JsonPropertyName("node_value")] string NodeValue,
    [property: JsonPropertyName("attributes")] string[] Attributes);

public sealed record BrowserDomSnapshot(
    [property: JsonPropertyName("cursor")] long Cursor,
    [property: JsonPropertyName("target_id")] string TargetId,
    [property: JsonPropertyName("target_incarnation")] long TargetIncarnation,
    [property: JsonPropertyName("observed_at")] DateTimeOffset ObservedAt,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("frames")] BrowserFrameState[] Frames,
    [property: JsonPropertyName("nodes")] BrowserNodeState[] Nodes);

public sealed record BrowserDownloadSnapshot(
    [property: JsonPropertyName("target_id")] string TargetId,
    [property: JsonPropertyName("target_incarnation")] long TargetIncarnation,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("artifact_id")] string ArtifactId,
    [property: JsonPropertyName("artifact_incarnation")] long ArtifactIncarnation,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size_bytes")] long SizeBytes,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("mime_type")] string? MimeType,
    [property: JsonPropertyName("storage_tier")] string StorageTier);