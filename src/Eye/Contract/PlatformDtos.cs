using System.Text.Json.Serialization;

namespace StealthEye.Contract;

public sealed record VolumeManifestResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("drive_type")] string DriveType,
    [property: JsonPropertyName("ready")] bool Ready,
    [property: JsonPropertyName("format"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Format,
    [property: JsonPropertyName("label"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Label,
    [property: JsonPropertyName("total_bytes"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? TotalBytes,
    [property: JsonPropertyName("free_bytes"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? FreeBytes);

public sealed record SoftwareManifestResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("path"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Path,
    [property: JsonPropertyName("version"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Version);

public sealed record OperationManifestResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("optional_external")] bool OptionalExternal,
    [property: JsonPropertyName("detail")] string Detail);

public sealed record MachineDescribeResult(
    [property: JsonPropertyName("machine")] string Machine,
    [property: JsonPropertyName("os")] string Os,
    [property: JsonPropertyName("os_architecture")] string OsArchitecture,
    [property: JsonPropertyName("process_architecture")] string ProcessArchitecture,
    [property: JsonPropertyName("logical_processors")] int LogicalProcessors,
    [property: JsonPropertyName("memory_total_bytes")] long MemoryTotalBytes,
    [property: JsonPropertyName("memory_available_bytes")] long MemoryAvailableBytes,
    [property: JsonPropertyName("ac_online"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? AcOnline,
    [property: JsonPropertyName("battery_percent"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? BatteryPercent,
    [property: JsonPropertyName("volumes")] VolumeManifestResult[] Volumes,
    [property: JsonPropertyName("software")] SoftwareManifestResult[] Software,
    [property: JsonPropertyName("operations")] OperationManifestResult[] Operations);

public sealed record SessionDescribeResult(
    [property: JsonPropertyName("host_session_id")] int HostSessionId,
    [property: JsonPropertyName("host_identity")] string HostIdentity,
    [property: JsonPropertyName("active_session_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ActiveSessionId,
    [property: JsonPropertyName("active_user"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ActiveUser);

public sealed record VolumeDescribeResult(
    [property: JsonPropertyName("volumes")] VolumeManifestResult[] Volumes);

public sealed record SoftwareFindArgs(
    [property: JsonPropertyName("query"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Query = null);

public sealed record SoftwareFindResult(
    [property: JsonPropertyName("software")] SoftwareManifestResult[] Software);

public sealed record SoftwareNameArgs(
    [property: JsonPropertyName("name")] string Name);

public sealed record SoftwareVersionResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("path"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Path,
    [property: JsonPropertyName("version"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Version);

public sealed record OperationListResult(
    [property: JsonPropertyName("operations")] OperationManifestResult[] Operations);

public sealed record OperationNameArgs(
    [property: JsonPropertyName("name")] string Name);

public sealed record OperationDescribeResult(
    [property: JsonPropertyName("operation")] OperationManifestResult Operation);

public sealed record MissionIdArgs(
    [property: JsonPropertyName("mission_id")] string MissionId);

public sealed record MissionCreateArgs(
    [property: JsonPropertyName("objective")] string Objective);

public sealed record MissionUpdateArgs(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("objective"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Objective = null,
    [property: JsonPropertyName("facts"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? Facts = null,
    [property: JsonPropertyName("decisions"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? Decisions = null,
    [property: JsonPropertyName("jobs"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? Jobs = null,
    [property: JsonPropertyName("triggers"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? Triggers = null,
    [property: JsonPropertyName("artifacts"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? Artifacts = null,
    [property: JsonPropertyName("questions"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? Questions = null,
    [property: JsonPropertyName("next_action"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextAction = null,
    [property: JsonPropertyName("clear_next_action")] bool ClearNextAction = false);

public sealed record RelayReadArgs(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("after_cursor")] long AfterCursor = 0,
    [property: JsonPropertyName("max_items")] int MaxItems = 64);

public sealed record RelaySendArgs(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("message")] string Message);

public sealed record MissionChatAssociateArgs(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("chat_ref")] string ChatRef,
    [property: JsonPropertyName("role"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Role = null,
    [property: JsonPropertyName("available")] bool Available = true);

public sealed record MissionChatRemoveArgs(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("chat_ref")] string ChatRef);

public sealed record MissionChatRemoveResult(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("chat_ref")] string ChatRef,
    [property: JsonPropertyName("removed")] bool Removed);

public sealed record ContextCaptureArgs(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("source")] string Source = "chatgpt",
    [property: JsonPropertyName("note"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note = null,
    [property: JsonPropertyName("include_screenshot")] bool IncludeScreenshot = false,
    [property: JsonPropertyName("screenshot_ocr")] bool ScreenshotOcr = false,
    [property: JsonPropertyName("uia_max_depth")] int UiaMaxDepth = 3,
    [property: JsonPropertyName("uia_max_nodes")] int UiaMaxNodes = 120);