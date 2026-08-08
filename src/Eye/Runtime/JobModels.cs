using System.Text.Json.Serialization;

namespace StealthEye.Runtime;

public static class JobStates
{
    public const string Starting = "starting";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string TimedOut = "timed_out";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
    public const string Interrupted = "interrupted";

    public static bool IsTerminal(string state) => state is Completed or TimedOut or Cancelled or Failed or Interrupted;
}

public sealed record JobPaths(string Directory, string Stdout, string Stderr);

public sealed record JobRecord(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("incarnation")] long Incarnation,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("context")] string Context,
    [property: JsonPropertyName("file_name")] string FileName,
    [property: JsonPropertyName("arguments")] string[] Arguments,
    [property: JsonPropertyName("working_directory")] string? WorkingDirectory,
    [property: JsonPropertyName("timeout_ms")] int TimeoutMs,
    [property: JsonPropertyName("terminal")] bool Terminal,
    [property: JsonPropertyName("columns")] int? Columns,
    [property: JsonPropertyName("rows")] int? Rows,
    [property: JsonPropertyName("pid")] int? Pid,
    [property: JsonPropertyName("effective_identity")] string? EffectiveIdentity,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("started_at")] DateTimeOffset? StartedAt,
    [property: JsonPropertyName("completed_at")] DateTimeOffset? CompletedAt,
    [property: JsonPropertyName("exit_code")] int? ExitCode,
    [property: JsonPropertyName("timed_out")] bool TimedOut,
    [property: JsonPropertyName("failure_code")] string? FailureCode,
    [property: JsonPropertyName("failure_message")] string? FailureMessage,
    [property: JsonPropertyName("stdout_path")] string StdoutPath,
    [property: JsonPropertyName("stderr_path")] string StderrPath);

public sealed record JobReadResult(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("stream")] string Stream,
    [property: JsonPropertyName("cursor")] long Cursor,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("next_cursor")] long NextCursor,
    [property: JsonPropertyName("eof")] bool Eof,
    [property: JsonPropertyName("state")] string State);

public sealed record JobWaitResult(
    [property: JsonPropertyName("job")] JobRecord Job,
    [property: JsonPropertyName("wait_timed_out")] bool WaitTimedOut);

public sealed record JobAttachSnapshot(JobRecord Job, long StdoutCursor, long StderrCursor);