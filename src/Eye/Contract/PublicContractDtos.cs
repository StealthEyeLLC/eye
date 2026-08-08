using System.Text.Json.Serialization;

namespace StealthEye.Contract;

public sealed record EmptyArgs;

public sealed record RunArgs(
    [property: JsonPropertyName("file_name")] string FileName,
    [property: JsonPropertyName("context")] string Context = "system",
    [property: JsonPropertyName("arguments")] string[]? Arguments = null,
    [property: JsonPropertyName("working_directory"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WorkingDirectory = null,
    [property: JsonPropertyName("timeout_ms")] int TimeoutMs = 30000);

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