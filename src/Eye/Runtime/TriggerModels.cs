using System.Text.Json.Serialization;

namespace StealthEye.Runtime;

public static class TriggerKinds
{
    public const string ProcessExit = "process_exit";
    public const string Time = "time";
}

public static class TriggerStates
{
    public const string Pending = "pending";
    public const string Satisfied = "satisfied";
    public const string TimedOut = "timed_out";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";

    public static bool IsTerminal(string state) => state is Satisfied or TimedOut or Cancelled or Failed;
}

public sealed record TriggerRecord(
    [property: JsonPropertyName("trigger_id")] string TriggerId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("completed_at")] DateTimeOffset? CompletedAt,
    [property: JsonPropertyName("deadline_at")] DateTimeOffset? DeadlineAt,
    [property: JsonPropertyName("process_id")] int? ProcessId,
    [property: JsonPropertyName("process_start_at")] DateTimeOffset? ProcessStartAt,
    [property: JsonPropertyName("due_at")] DateTimeOffset? DueAt,
    [property: JsonPropertyName("failure_message")] string? FailureMessage);

public sealed record TriggerEvent(
    [property: JsonPropertyName("trigger_id")] string TriggerId,
    [property: JsonPropertyName("sequence")] long Sequence,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("payload_json")] string PayloadJson);

public sealed record TriggerWaitResult(
    [property: JsonPropertyName("trigger")] TriggerRecord Trigger,
    [property: JsonPropertyName("wait_timed_out")] bool WaitTimedOut);

public sealed record TriggerReadResult(
    [property: JsonPropertyName("trigger_id")] string TriggerId,
    [property: JsonPropertyName("cursor")] long Cursor,
    [property: JsonPropertyName("events")] TriggerEvent[] Events,
    [property: JsonPropertyName("next_cursor")] long NextCursor,
    [property: JsonPropertyName("eof")] bool Eof);
