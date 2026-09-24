using System.Text.Json.Serialization;

namespace StealthEye.Contract;

public sealed record UiObserveArgs(
    [property: JsonPropertyName("include_invisible")] bool IncludeInvisible = false);

public sealed record UiWindowBoundsResult(
    [property: JsonPropertyName("left")] int Left,
    [property: JsonPropertyName("top")] int Top,
    [property: JsonPropertyName("right")] int Right,
    [property: JsonPropertyName("bottom")] int Bottom);

public sealed record UiWindowResult(
    [property: JsonPropertyName("window_id")] string WindowId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("process_id")] int ProcessId,
    [property: JsonPropertyName("process_name")] string ProcessName,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("class_name")] string ClassName,
    [property: JsonPropertyName("visible")] bool Visible,
    [property: JsonPropertyName("minimized")] bool Minimized,
    [property: JsonPropertyName("foreground")] bool Foreground,
    [property: JsonPropertyName("bounds")] UiWindowBoundsResult Bounds);

public sealed record UiObserveResult(
    [property: JsonPropertyName("cursor")] long Cursor,
    [property: JsonPropertyName("session_id")] int SessionId,
    [property: JsonPropertyName("observed_at")] DateTimeOffset ObservedAt,
    [property: JsonPropertyName("windows")] UiWindowResult[] Windows);