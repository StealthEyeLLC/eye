using System.Text.Json.Serialization;

namespace StealthEye.Runtime;

public sealed record MissionRelayEntry(
    [property: JsonPropertyName("relay_id")] string RelayId,
    [property: JsonPropertyName("cursor")] long Cursor,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record MissionBlackboardRecord(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("objective")] string Objective,
    [property: JsonPropertyName("facts")] string[] Facts,
    [property: JsonPropertyName("decisions")] string[] Decisions,
    [property: JsonPropertyName("jobs")] string[] Jobs,
    [property: JsonPropertyName("triggers")] string[] Triggers,
    [property: JsonPropertyName("artifacts")] string[] Artifacts,
    [property: JsonPropertyName("questions")] string[] Questions,
    [property: JsonPropertyName("next_action")] string? NextAction,
    [property: JsonPropertyName("relay")] MissionRelayEntry[] Relay,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

public sealed record MissionBlackboardUpdate(
    string? Objective = null,
    string[]? Facts = null,
    string[]? Decisions = null,
    string[]? Jobs = null,
    string[]? Triggers = null,
    string[]? Artifacts = null,
    string[]? Questions = null,
    string? NextAction = null,
    bool ClearNextAction = false);