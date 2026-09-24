using System.Text.Json;
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
    public const string ActUia = "desktop.uia_act";
    public const string ArmUiaChange = "desktop.uia_arm_change";
    public const string WaitUiaChange = "desktop.uia_wait_change";
    public const string EnsureBrowser = "browser.ensure";
    public const string ObserveBrowserTargets = "browser.targets";
    public const string NavigateBrowserTarget = "browser.navigate";
    public const string EvaluateBrowserTarget = "browser.evaluate";
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
public sealed record WorkerUiaActionRequest(
    [property: JsonPropertyName("hwnd")] long Hwnd,
    [property: JsonPropertyName("runtime_id")] string RuntimeId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("value")] string? Value = null,
    [property: JsonPropertyName("max_nodes")] int MaxNodes = 5000);

public sealed record WorkerUiaActionResult(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("completed")] bool Completed);
public sealed record WorkerUiaWaitRequest(
    [property: JsonPropertyName("hwnd")] long Hwnd,
    [property: JsonPropertyName("runtime_id")] string? RuntimeId,
    [property: JsonPropertyName("event_types")] string[] EventTypes,
    [property: JsonPropertyName("max_nodes")] int MaxNodes = 5000);

public sealed record WorkerUiaArmResult(
    [property: JsonPropertyName("armed")] bool Armed);

public sealed record WorkerUiaChangeResult(
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("runtime_id")] string RuntimeId,
    [property: JsonPropertyName("property")] string? Property,
    [property: JsonPropertyName("value")] string? Value);public sealed record WorkerBrowserEnsureRequest(
    [property: JsonPropertyName("chrome_path")] string? ChromePath = null,
    [property: JsonPropertyName("user_data_dir")] string? UserDataDir = null,
    [property: JsonPropertyName("initial_url")] string InitialUrl = "about:blank",
    [property: JsonPropertyName("headless")] bool Headless = true);

public sealed record WorkerBrowserStatusResult(
    [property: JsonPropertyName("chrome_path")] string ChromePath,
    [property: JsonPropertyName("user_data_dir")] string UserDataDir,
    [property: JsonPropertyName("process_id")] int ProcessId,
    [property: JsonPropertyName("debug_port")] int DebugPort,
    [property: JsonPropertyName("browser_version")] string BrowserVersion,
    [property: JsonPropertyName("protocol_version")] string ProtocolVersion);

public sealed record WorkerBrowserTargetInfo(
    [property: JsonPropertyName("cdp_target_id")] string CdpTargetId,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("url")] string Url);

public sealed record WorkerBrowserTargetsResult(
    [property: JsonPropertyName("observed_at")] DateTimeOffset ObservedAt,
    [property: JsonPropertyName("browser_version")] string BrowserVersion,
    [property: JsonPropertyName("protocol_version")] string ProtocolVersion,
    [property: JsonPropertyName("targets")] WorkerBrowserTargetInfo[] Targets);
public sealed record WorkerBrowserNavigateRequest(
    [property: JsonPropertyName("cdp_target_id")] string CdpTargetId,
    [property: JsonPropertyName("url")] string Url);

public sealed record WorkerBrowserNavigateResult(
    [property: JsonPropertyName("cdp_target_id")] string CdpTargetId,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("frame_id")] string? FrameId,
    [property: JsonPropertyName("loader_id")] string? LoaderId,
    [property: JsonPropertyName("error_text")] string? ErrorText);

public sealed record WorkerBrowserEvaluateRequest(
    [property: JsonPropertyName("cdp_target_id")] string CdpTargetId,
    [property: JsonPropertyName("expression")] string Expression);

public sealed record WorkerBrowserEvaluateResult(
    [property: JsonPropertyName("cdp_target_id")] string CdpTargetId,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value_json")] string? ValueJson,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("threw")] bool Threw,
    [property: JsonPropertyName("exception_text")] string? ExceptionText);
public sealed record WorkerShutdownResult(
    [property: JsonPropertyName("stopping")] bool Stopping);
