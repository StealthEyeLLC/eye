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