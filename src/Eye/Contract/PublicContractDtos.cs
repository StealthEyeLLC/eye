using System.Text.Json.Serialization;

namespace StealthEye.Contract;

public sealed record EmptyArgs;

public sealed record RunArgs(
    [property: JsonPropertyName("file_name")] string FileName,
    [property: JsonPropertyName("context")] string Context = "system",
    [property: JsonPropertyName("arguments")] string[]? Arguments = null,
    [property: JsonPropertyName("working_directory"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WorkingDirectory = null,
    [property: JsonPropertyName("timeout_ms")] int TimeoutMs = 30000);

public sealed record JobStartArgs(
    [property: JsonPropertyName("file_name")] string FileName,
    [property: JsonPropertyName("context")] string Context = "system",
    [property: JsonPropertyName("arguments")] string[]? Arguments = null,
    [property: JsonPropertyName("working_directory"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WorkingDirectory = null,
    [property: JsonPropertyName("timeout_ms")] int TimeoutMs = 30000,
    [property: JsonPropertyName("terminal")] bool Terminal = false,
    [property: JsonPropertyName("columns")] int Columns = 120,
    [property: JsonPropertyName("rows")] int Rows = 30);

public sealed record JobWriteArgs(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("text")] string Text);

public sealed record JobResizeArgs(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("columns")] int Columns,
    [property: JsonPropertyName("rows")] int Rows);

public sealed record JobIdArgs(
    [property: JsonPropertyName("job_id")] string JobId);

public sealed record JobReadArgs(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("stream")] string Stream = "stdout",
    [property: JsonPropertyName("cursor")] long Cursor = 0,
    [property: JsonPropertyName("max_bytes")] int MaxBytes = 65536);

public sealed record JobWaitArgs(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("wait_ms")] int WaitMs = 30000);

public sealed record ArtifactIdArgs(
    [property: JsonPropertyName("artifact_id")] string ArtifactId);

public sealed record ArtifactPreviewArgs(
    [property: JsonPropertyName("artifact_id")] string ArtifactId,
    [property: JsonPropertyName("max_chars")] int MaxChars = 4000);

public sealed record ArtifactReadRangeArgs(
    [property: JsonPropertyName("artifact_id")] string ArtifactId,
    [property: JsonPropertyName("offset")] long Offset = 0,
    [property: JsonPropertyName("max_bytes")] int MaxBytes = 65536);

public sealed record ArtifactDiffArgs(
    [property: JsonPropertyName("left_artifact_id")] string LeftArtifactId,
    [property: JsonPropertyName("right_artifact_id")] string RightArtifactId);

public sealed record ArtifactExportArgs(
    [property: JsonPropertyName("artifact_id")] string ArtifactId,
    [property: JsonPropertyName("destination")] string Destination,
    [property: JsonPropertyName("overwrite")] bool Overwrite = false);

public sealed record ArtifactInfoResult(
    [property: JsonPropertyName("artifact_id")] string ArtifactId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("mime_type"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MimeType,
    [property: JsonPropertyName("size_bytes")] long SizeBytes,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("storage_tier")] string StorageTier,
    [property: JsonPropertyName("provenance")] string Provenance,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record ArtifactPreviewPublicResult(
    [property: JsonPropertyName("artifact_id")] string ArtifactId,
    [property: JsonPropertyName("text_available")] bool TextAvailable,
    [property: JsonPropertyName("text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text,
    [property: JsonPropertyName("truncated")] bool Truncated);

public sealed record ArtifactReadRangeResult(
    [property: JsonPropertyName("artifact_id")] string ArtifactId,
    [property: JsonPropertyName("offset")] long Offset,
    [property: JsonPropertyName("bytes_read")] int BytesRead,
    [property: JsonPropertyName("next_offset")] long NextOffset,
    [property: JsonPropertyName("eof")] bool Eof,
    [property: JsonPropertyName("data_base64")] string DataBase64);

public sealed record ArtifactExportResult(
    [property: JsonPropertyName("artifact_id")] string ArtifactId,
    [property: JsonPropertyName("destination")] string Destination,
    [property: JsonPropertyName("size_bytes")] long SizeBytes,
    [property: JsonPropertyName("sha256")] string Sha256);

public sealed record ArtifactDeleteResult(
    [property: JsonPropertyName("artifact_id")] string ArtifactId,
    [property: JsonPropertyName("deleted")] bool Deleted);

public sealed record ArtifactDiffPublicResult(
    [property: JsonPropertyName("left_artifact_id")] string LeftArtifactId,
    [property: JsonPropertyName("right_artifact_id")] string RightArtifactId,
    [property: JsonPropertyName("equal")] bool Equal,
    [property: JsonPropertyName("left_size_bytes")] long LeftSizeBytes,
    [property: JsonPropertyName("right_size_bytes")] long RightSizeBytes,
    [property: JsonPropertyName("left_sha256")] string LeftSha256,
    [property: JsonPropertyName("right_sha256")] string RightSha256,
    [property: JsonPropertyName("first_difference_offset"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? FirstDifferenceOffset);
public sealed record SystemStatusResult(
    [property: JsonPropertyName("product")] string Product,
    [property: JsonPropertyName("executable")] string Executable,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("contract")] string Contract,
    [property: JsonPropertyName("process_id")] int ProcessId,
    [property: JsonPropertyName("machine")] string Machine,
    [property: JsonPropertyName("identity")] string Identity,
    [property: JsonPropertyName("started_at")] DateTimeOffset StartedAt);

public sealed record CapabilityFacades(
    [property: JsonPropertyName("eye_inspect")] string[] EyeInspect,
    [property: JsonPropertyName("eye_run")] string[] EyeRun,
    [property: JsonPropertyName("eye_change")] string[] EyeChange,
    [property: JsonPropertyName("eye_interact")] string[] EyeInteract,
    [property: JsonPropertyName("eye_external")] string[] EyeExternal,
    [property: JsonPropertyName("eye_live")] string[] EyeLive);

public sealed record CapabilitiesResult(
    [property: JsonPropertyName("contract")] string Contract,
    [property: JsonPropertyName("facades")] CapabilityFacades Facades);

public sealed record RunOperationResult(
    [property: JsonPropertyName("pid")] int Pid,
    [property: JsonPropertyName("exit_code")] int ExitCode,
    [property: JsonPropertyName("timed_out")] bool TimedOut,
    [property: JsonPropertyName("stdout")] string Stdout,
    [property: JsonPropertyName("stderr")] string Stderr,
    [property: JsonPropertyName("context")] string Context,
    [property: JsonPropertyName("effective_identity")] string EffectiveIdentity,
    [property: JsonPropertyName("duration_ms")] long DurationMs);

public sealed record JobReferenceResult(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("state")] string State);

public sealed record JobStatusResult(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("context")] string Context,
    [property: JsonPropertyName("terminal")] bool Terminal,
    [property: JsonPropertyName("columns"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Columns,
    [property: JsonPropertyName("rows"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Rows,
    [property: JsonPropertyName("pid"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Pid,
    [property: JsonPropertyName("effective_identity"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EffectiveIdentity,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("started_at"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? StartedAt,
    [property: JsonPropertyName("completed_at"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? CompletedAt,
    [property: JsonPropertyName("exit_code"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ExitCode,
    [property: JsonPropertyName("timed_out")] bool TimedOut,
    [property: JsonPropertyName("failure_code"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FailureCode,
    [property: JsonPropertyName("failure_message"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FailureMessage);

public sealed record JobReadPublicResult(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("stream")] string Stream,
    [property: JsonPropertyName("cursor")] long Cursor,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("next_cursor")] long NextCursor,
    [property: JsonPropertyName("eof")] bool Eof,
    [property: JsonPropertyName("state")] string State);

public sealed record JobWaitPublicResult(
    [property: JsonPropertyName("job")] JobStatusResult Job,
    [property: JsonPropertyName("wait_timed_out")] bool WaitTimedOut);

public sealed record JobCancelResult(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("state")] string State);

public sealed record JobWriteResult(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("bytes_written")] int BytesWritten,
    [property: JsonPropertyName("state")] string State);

public sealed record JobResizeResult(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("columns")] int Columns,
    [property: JsonPropertyName("rows")] int Rows,
    [property: JsonPropertyName("state")] string State);

public sealed record JobAttachResult(
    [property: JsonPropertyName("job")] JobStatusResult Job,
    [property: JsonPropertyName("stdout_cursor")] long StdoutCursor,
    [property: JsonPropertyName("stderr_cursor")] long StderrCursor);

public sealed record EyeError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("retryable")] bool Retryable,
    [property: JsonPropertyName("expected"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Expected = null);

public sealed record EyeSuccess<T>(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("result")] T Result);

public sealed record EyeFailure(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("error")] EyeError Error);
