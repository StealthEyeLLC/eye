using System.Text.Json.Serialization;

namespace StealthEye.Contract;

public sealed record EmptyArgs;

public sealed record RunArgs(
    [property: JsonPropertyName("file_name")] string FileName,
    [property: JsonPropertyName("context")] string Context = "system",
    [property: JsonPropertyName("arguments")] string[]? Arguments = null,
    [property: JsonPropertyName("working_directory"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WorkingDirectory = null,
    [property: JsonPropertyName("timeout_ms")] int TimeoutMs = 30000);

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
