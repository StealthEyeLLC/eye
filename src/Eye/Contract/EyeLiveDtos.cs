using System.Text.Json.Serialization;

namespace StealthEye.Contract;

public sealed record EyeLiveMachineResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("identity")] string Identity);

public sealed record EyeLiveContextResult(
    [property: JsonPropertyName("mission_count")] int MissionCount,
    [property: JsonPropertyName("active_job_count")] int ActiveJobCount,
    [property: JsonPropertyName("pending_trigger_count")] int PendingTriggerCount,
    [property: JsonPropertyName("relay_message_count")] int RelayMessageCount,
    [property: JsonPropertyName("latest_mission_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LatestMissionId);

public sealed record EyeLiveEngineResult(
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("active_version"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ActiveVersion,
    [property: JsonPropertyName("previous_version"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PreviousVersion,
    [property: JsonPropertyName("engine_version"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EngineVersion,
    [property: JsonPropertyName("process_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ProcessId,
    [property: JsonPropertyName("last_error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LastError);

public sealed record EyeLiveMissionResult(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("objective")] string Objective,
    [property: JsonPropertyName("next_action"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextAction,
    [property: JsonPropertyName("relay_count")] int RelayCount,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

public sealed record EyeLiveRelayResult(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("cursor")] long Cursor,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record EyeLiveJobResult(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("context")] string Context,
    [property: JsonPropertyName("terminal")] bool Terminal,
    [property: JsonPropertyName("pid"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Pid,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("completed_at"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? CompletedAt,
    [property: JsonPropertyName("failure_message"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FailureMessage,
    [property: JsonPropertyName("stdout_tail")] string StdoutTail,
    [property: JsonPropertyName("stderr_tail")] string StderrTail);

public sealed record EyeLiveTriggerResult(
    [property: JsonPropertyName("trigger_id")] string TriggerId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("deadline_at"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeadlineAt,
    [property: JsonPropertyName("process_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ProcessId,
    [property: JsonPropertyName("file_path"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FilePath,
    [property: JsonPropertyName("failure_message"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FailureMessage);

public sealed record EyeLiveArtifactResult(
    [property: JsonPropertyName("artifact_id")] string ArtifactId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size_bytes")] long SizeBytes,
    [property: JsonPropertyName("storage_tier")] string StorageTier,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record EyeLiveSnapshotResult(
    [property: JsonPropertyName("generated_at")] DateTimeOffset GeneratedAt,
    [property: JsonPropertyName("machine")] EyeLiveMachineResult Machine,
    [property: JsonPropertyName("context")] EyeLiveContextResult Context,
    [property: JsonPropertyName("engine")] EyeLiveEngineResult Engine,
    [property: JsonPropertyName("missions")] EyeLiveMissionResult[] Missions,
    [property: JsonPropertyName("relay")] EyeLiveRelayResult[] Relay,
    [property: JsonPropertyName("jobs")] EyeLiveJobResult[] Jobs,
    [property: JsonPropertyName("triggers")] EyeLiveTriggerResult[] Triggers,
    [property: JsonPropertyName("artifacts")] EyeLiveArtifactResult[] Artifacts);

public sealed record EyeLiveAppActionArgs(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("job_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? JobId = null);

public sealed record EyeLiveAppActionResult(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("completed")] bool Completed,
    [property: JsonPropertyName("snapshot")] EyeLiveSnapshotResult Snapshot);
