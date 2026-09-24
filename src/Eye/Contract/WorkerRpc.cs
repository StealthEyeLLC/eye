using System.Text.Json.Serialization;

namespace StealthEye.Contract;

public static class WorkerRpcMethods
{
    public const string CurrentProtocolVersion = "1.0.0";
    public const string Handshake = "worker.handshake";
    public const string StartTerminal = "terminal.start";
    public const string WriteTerminal = "terminal.write";
    public const string ResizeTerminal = "terminal.resize";
    public const string WaitTerminal = "terminal.wait";
    public const string ObserveWindows = "desktop.windows";
    public const string QueryUia = "desktop.uia_query";
    public const string Shutdown = "worker.shutdown";
}

public sealed record SessionWorkerHandshake(
    [property: JsonPropertyName("worker_protocol_version")] string WorkerProtocolVersion,
    [property: JsonPropertyName("worker_version")] string WorkerVersion,
    [property: JsonPropertyName("process_id")] int ProcessId,
    [property: JsonPropertyName("session_id")] int SessionId);

public sealed record WorkerTerminalStartRequest(
    [property: JsonPropertyName("context")] string Context,
    [property: JsonPropertyName("file_name")] string FileName,
    [property: JsonPropertyName("arguments")] string[] Arguments,
    [property: JsonPropertyName("working_directory")] string? WorkingDirectory,
    [property: JsonPropertyName("timeout_ms")] int TimeoutMs,
    [property: JsonPropertyName("columns")] int Columns,
    [property: JsonPropertyName("rows")] int Rows);

public sealed record WorkerTerminalStartResult(
    [property: JsonPropertyName("process_id")] int ProcessId,
    [property: JsonPropertyName("effective_identity")] string EffectiveIdentity);

public sealed record WorkerTerminalWriteRequest(
    [property: JsonPropertyName("text")] string Text);

public sealed record WorkerTerminalWriteResult(
    [property: JsonPropertyName("bytes_written")] int BytesWritten);

public sealed record WorkerTerminalResizeRequest(
    [property: JsonPropertyName("columns")] int Columns,
    [property: JsonPropertyName("rows")] int Rows);

public sealed record WorkerTerminalResizeResult(
    [property: JsonPropertyName("columns")] int Columns,
    [property: JsonPropertyName("rows")] int Rows);

public sealed record WorkerTerminalExitResult(
    [property: JsonPropertyName("exit_code")] int ExitCode,
    [property: JsonPropertyName("timed_out")] bool TimedOut,
    [property: JsonPropertyName("duration_ms")] long DurationMs);

public sealed record WorkerDesktopObserveRequest(
    [property: JsonPropertyName("include_invisible")] bool IncludeInvisible = false);

public sealed record WorkerWindowRect(
    [property: JsonPropertyName("left")] int Left,
    [property: JsonPropertyName("top")] int Top,
    [property: JsonPropertyName("right")] int Right,
    [property: JsonPropertyName("bottom")] int Bottom);

public sealed record WorkerUiaWindowRoot(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("automation_id")] string AutomationId,
    [property: JsonPropertyName("control_type")] string ControlType,
    [property: JsonPropertyName("framework_id")] string FrameworkId,
    [property: JsonPropertyName("class_name")] string ClassName,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("offscreen")] bool Offscreen);
public sealed record WorkerWindowInfo(
    [property: JsonPropertyName("hwnd")] long Hwnd,
    [property: JsonPropertyName("process_id")] int ProcessId,
    [property: JsonPropertyName("process_start_at")] DateTimeOffset? ProcessStartAt,
    [property: JsonPropertyName("process_name")] string ProcessName,
    [property: JsonPropertyName("thread_id")] int ThreadId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("class_name")] string ClassName,
    [property: JsonPropertyName("visible")] bool Visible,
    [property: JsonPropertyName("minimized")] bool Minimized,
    [property: JsonPropertyName("foreground")] bool Foreground,
    [property: JsonPropertyName("bounds")] WorkerWindowRect Bounds,
    [property: JsonPropertyName("uia")] WorkerUiaWindowRoot? Uia = null);

public sealed record WorkerDesktopObservationResult(
    [property: JsonPropertyName("session_id")] int SessionId,
    [property: JsonPropertyName("observed_at")] DateTimeOffset ObservedAt,
    [property: JsonPropertyName("windows")] WorkerWindowInfo[] Windows);
public sealed record WorkerUiaQueryRequest(
    [property: JsonPropertyName("hwnd")] long Hwnd,
    [property: JsonPropertyName("max_depth")] int MaxDepth = 4,
    [property: JsonPropertyName("max_nodes")] int MaxNodes = 200);

public sealed record WorkerUiaElementInfo(
    [property: JsonPropertyName("runtime_id")] string RuntimeId,
    [property: JsonPropertyName("parent_runtime_id")] string? ParentRuntimeId,
    [property: JsonPropertyName("depth")] int Depth,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("automation_id")] string AutomationId,
    [property: JsonPropertyName("control_type")] string ControlType,
    [property: JsonPropertyName("framework_id")] string FrameworkId,
    [property: JsonPropertyName("class_name")] string ClassName,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("offscreen")] bool Offscreen,
    [property: JsonPropertyName("focused")] bool Focused,
    [property: JsonPropertyName("bounds")] WorkerWindowRect Bounds);

public sealed record WorkerUiaQueryResult(
    [property: JsonPropertyName("observed_at")] DateTimeOffset ObservedAt,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("elements")] WorkerUiaElementInfo[] Elements);
public sealed record WorkerShutdownResult(
    [property: JsonPropertyName("stopping")] bool Stopping);
