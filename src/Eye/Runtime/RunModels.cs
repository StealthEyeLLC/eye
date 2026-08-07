using System.Text.Json.Serialization;

namespace StealthEye.Runtime;

public sealed class RunRequest
{
    [JsonPropertyName("context")]
    public string Context { get; init; } = "system";

    [JsonPropertyName("file_name")]
    public string FileName { get; init; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string[] Arguments { get; init; } = [];

    [JsonPropertyName("working_directory")]
    public string? WorkingDirectory { get; init; }

    [JsonPropertyName("timeout_ms")]
    public int TimeoutMs { get; init; } = 30000;
}

public sealed record ProcessRunResult(
    int Pid,
    int ExitCode,
    bool TimedOut,
    string Stdout,
    string Stderr,
    string Context,
    string EffectiveIdentity,
    long DurationMs);
