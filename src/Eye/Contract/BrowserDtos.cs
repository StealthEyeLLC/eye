using System.Text.Json;
using System.Text.Json.Serialization;

namespace StealthEye.Contract;

public sealed record BrowserTargetResult(
    [property: JsonPropertyName("target_id")] string TargetId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("url")] string Url);

public sealed record BrowserObserveResult(
    [property: JsonPropertyName("cursor")] long Cursor,
    [property: JsonPropertyName("observed_at")] DateTimeOffset ObservedAt,
    [property: JsonPropertyName("browser_version")] string BrowserVersion,
    [property: JsonPropertyName("protocol_version")] string ProtocolVersion,
    [property: JsonPropertyName("targets")] BrowserTargetResult[] Targets);

public sealed record BrowserNavigateArgs(
    [property: JsonPropertyName("target_id")] string TargetId,
    [property: JsonPropertyName("url")] string Url);

public sealed record BrowserNavigateResult(
    [property: JsonPropertyName("target_id")] string TargetId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("frame_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FrameId,
    [property: JsonPropertyName("loader_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LoaderId,
    [property: JsonPropertyName("error_text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ErrorText);

public sealed record BrowserEvaluateArgs(
    [property: JsonPropertyName("target_id")] string TargetId,
    [property: JsonPropertyName("expression")] string Expression);

public sealed record BrowserEvaluateResult(
    [property: JsonPropertyName("target_id")] string TargetId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Value,
    [property: JsonPropertyName("description"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description,
    [property: JsonPropertyName("threw")] bool Threw,
    [property: JsonPropertyName("exception_text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExceptionText);
public sealed record BrowserDomArgs(
    [property: JsonPropertyName("target_id")] string TargetId,
    [property: JsonPropertyName("max_depth")] int MaxDepth = 4,
    [property: JsonPropertyName("max_nodes")] int MaxNodes = 500);

public sealed record BrowserFrameResult(
    [property: JsonPropertyName("frame_id")] string FrameId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("parent_frame_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ParentFrameId,
    [property: JsonPropertyName("loader_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LoaderId,
    [property: JsonPropertyName("url")] string Url);

public sealed record BrowserNodeResult(
    [property: JsonPropertyName("node_id")] string NodeId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("parent_node_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ParentNodeId,
    [property: JsonPropertyName("frame_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FrameId,
    [property: JsonPropertyName("node_type")] int NodeType,
    [property: JsonPropertyName("node_name")] string NodeName,
    [property: JsonPropertyName("node_value")] string NodeValue,
    [property: JsonPropertyName("attributes")] string[] Attributes);

public sealed record BrowserDomResult(
    [property: JsonPropertyName("cursor")] long Cursor,
    [property: JsonPropertyName("target_id")] string TargetId,
    [property: JsonPropertyName("target_incarnation")] long TargetIncarnation,
    [property: JsonPropertyName("observed_at")] DateTimeOffset ObservedAt,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("frames")] BrowserFrameResult[] Frames,
    [property: JsonPropertyName("nodes")] BrowserNodeResult[] Nodes);

public sealed record BrowserDownloadArgs(
    [property: JsonPropertyName("target_id")] string TargetId,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("timeout_ms")] int TimeoutMs = 30000);

public sealed record BrowserDownloadResult(
    [property: JsonPropertyName("target_id")] string TargetId,
    [property: JsonPropertyName("target_incarnation")] long TargetIncarnation,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("artifact_id")] string ArtifactId,
    [property: JsonPropertyName("artifact_incarnation")] long ArtifactIncarnation,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size_bytes")] long SizeBytes,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("mime_type"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MimeType,
    [property: JsonPropertyName("storage_tier")] string StorageTier);