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