using System.Security.Principal;
using System.Text.Json;

namespace StealthEye.Runtime;

public sealed class EyeDispatcher(ProcessRunner processRunner)
{
    public async Task<object> ExecuteAsync(string op, JsonElement? args, CancellationToken cancellationToken = default)
    {
        try
        {
            switch (op)
            {
                case "system.status":
                    return new
                    {
                        ok = true,
                        result = new
                        {
                            product = "StealthEye",
                            executable = "eye",
                            version = typeof(EyeDispatcher).Assembly.GetName().Version?.ToString() ?? "unknown",
                            process_id = Environment.ProcessId,
                            machine = Environment.MachineName,
                            identity = WindowsIdentity.GetCurrent().Name,
                            started_at = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()
                        }
                    };

                case "capabilities":
                    return new
                    {
                        ok = true,
                        result = new
                        {
                            operations = new[]
                            {
                                "system.status",
                                "capabilities",
                                "run"
                            }
                        }
                    };

                case "run":
                {
                    if (args is null || args.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                        throw new ArgumentException("run requires args.");

                    var request = args.Value.Deserialize<RunRequest>()
                        ?? throw new ArgumentException("Unable to deserialize run args.");
                    var result = await processRunner.RunAsync(request, cancellationToken);

                    return new
                    {
                        ok = true,
                        result = new
                        {
                            pid = result.Pid,
                            exit_code = result.ExitCode,
                            timed_out = result.TimedOut,
                            stdout = result.Stdout,
                            stderr = result.Stderr,
                            context = result.Context,
                            effective_identity = result.EffectiveIdentity,
                            duration_ms = result.DurationMs
                        }
                    };
                }

                default:
                    return new
                    {
                        ok = false,
                        error = new
                        {
                            code = "unknown_operation",
                            message = $"Unknown Eye operation: {op}"
                        }
                    };
            }
        }
        catch (Exception ex)
        {
            return new
            {
                ok = false,
                error = new
                {
                    code = "operation_failed",
                    message = ex.Message,
                    type = ex.GetType().FullName
                }
            };
        }
    }
}
