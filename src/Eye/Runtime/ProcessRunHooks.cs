namespace StealthEye.Runtime;

public enum ProcessOutputChannel
{
    Stdout,
    Stderr
}

public sealed class ProcessRunHooks
{
    public Action<int, string>? Started { get; init; }
    public Func<ProcessOutputChannel, string, ValueTask>? Output { get; init; }
    public bool CaptureOutput { get; init; } = true;
}
