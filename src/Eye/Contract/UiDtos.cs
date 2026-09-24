using System.Text.Json.Serialization;

namespace StealthEye.Contract;

public sealed record UiObserveArgs(
    [property: JsonPropertyName("include_invisible")] bool IncludeInvisible = false);

public sealed record UiWindowBoundsResult(
    [property: JsonPropertyName("left")] int Left,
    [property: JsonPropertyName("top")] int Top,
    [property: JsonPropertyName("right")] int Right,
    [property: JsonPropertyName("bottom")] int Bottom);

public sealed record UiUiaRootResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("automation_id")] string AutomationId,
    [property: JsonPropertyName("control_type")] string ControlType,
    [property: JsonPropertyName("framework_id")] string FrameworkId,
    [property: JsonPropertyName("class_name")] string ClassName,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("offscreen")] bool Offscreen);
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
    [property: JsonPropertyName("bounds")] UiWindowBoundsResult Bounds,
    [property: JsonPropertyName("uia"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] UiUiaRootResult? Uia);

public sealed record UiObserveResult(
    [property: JsonPropertyName("cursor")] long Cursor,
    [property: JsonPropertyName("session_id")] int SessionId,
    [property: JsonPropertyName("observed_at")] DateTimeOffset ObservedAt,
    [property: JsonPropertyName("windows")] UiWindowResult[] Windows);
public sealed record UiQueryArgs(
    [property: JsonPropertyName("window_id")] string WindowId,
    [property: JsonPropertyName("max_depth")] int MaxDepth = 4,
    [property: JsonPropertyName("max_nodes")] int MaxNodes = 200);

public sealed record UiElementResult(
    [property: JsonPropertyName("element_id")] string ElementId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("parent_element_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ParentElementId,
    [property: JsonPropertyName("depth")] int Depth,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("automation_id")] string AutomationId,
    [property: JsonPropertyName("control_type")] string ControlType,
    [property: JsonPropertyName("framework_id")] string FrameworkId,
    [property: JsonPropertyName("class_name")] string ClassName,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("offscreen")] bool Offscreen,
    [property: JsonPropertyName("focused")] bool Focused,
    [property: JsonPropertyName("bounds")] UiWindowBoundsResult Bounds);

public sealed record UiQueryResult(
    [property: JsonPropertyName("cursor")] long Cursor,
    [property: JsonPropertyName("window_id")] string WindowId,
    [property: JsonPropertyName("window_incarnation")] long WindowIncarnation,
    [property: JsonPropertyName("observed_at")] DateTimeOffset ObservedAt,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("elements")] UiElementResult[] Elements);
