using System.Text.Json.Serialization;

namespace StealthEye.Runtime;

public sealed record UiaElementState(
    [property: JsonPropertyName("element_id")] string ElementId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("parent_element_id")] string? ParentElementId,
    [property: JsonPropertyName("depth")] int Depth,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("automation_id")] string AutomationId,
    [property: JsonPropertyName("control_type")] string ControlType,
    [property: JsonPropertyName("framework_id")] string FrameworkId,
    [property: JsonPropertyName("class_name")] string ClassName,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("offscreen")] bool Offscreen,
    [property: JsonPropertyName("focused")] bool Focused,
    [property: JsonPropertyName("left")] int Left,
    [property: JsonPropertyName("top")] int Top,
    [property: JsonPropertyName("right")] int Right,
    [property: JsonPropertyName("bottom")] int Bottom);

public sealed record UiaQuerySnapshot(
    [property: JsonPropertyName("cursor")] long Cursor,
    [property: JsonPropertyName("window_id")] string WindowId,
    [property: JsonPropertyName("window_incarnation")] long WindowIncarnation,
    [property: JsonPropertyName("observed_at")] DateTimeOffset ObservedAt,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("elements")] UiaElementState[] Elements);