using System.Text.Json.Serialization;

namespace StealthEye.Runtime;

public sealed record DesktopWindowState(
    [property: JsonPropertyName("window_id")] string WindowId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
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
    [property: JsonPropertyName("left")] int Left,
    [property: JsonPropertyName("top")] int Top,
    [property: JsonPropertyName("right")] int Right,
    [property: JsonPropertyName("bottom")] int Bottom);

public sealed record DesktopWindowSnapshot(
    [property: JsonPropertyName("cursor")] long Cursor,
    [property: JsonPropertyName("session_id")] int SessionId,
    [property: JsonPropertyName("observed_at")] DateTimeOffset ObservedAt,
    [property: JsonPropertyName("windows")] DesktopWindowState[] Windows);