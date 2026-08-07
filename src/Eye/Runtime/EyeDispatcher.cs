using System.Security.Principal;
using System.Text.Json;

namespace StealthEye.Runtime;

public enum EyeEffectClass
{
    Inspect,
    Run,
    Change,
    Interact,
    External
}

public sealed class EyeDispatcher(ProcessRunner processRunner)
{
    public async Task<object> ExecuteAsync(
        EyeEffectClass effectClass,
        string op,
        JsonElement? args,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var requiredClass = GetEffectClass(op);
            if (requiredClass is not null && requiredClass.Value != effectClass)
            {
                return new
                {
                    ok = false,
                    error = new
                    {
                        code = "wrong_tool",
                        message = $"Operation '{op}' belongs to {GetFacadeName(requiredClass.Value)}, not {GetFacadeName(effectClass)}.",
                        retryable = true,
                        expected = new
                        {
                            tool = GetFacadeName(requiredClass.Value)
                        }
                    }
                };
            }

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
                            contract = "eye-mcp-v1",
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
                            contract = "eye-mcp-v1",
                            facades = new
                            {
                                eye_inspect = new[]
                                {
                                    "system.status",
                                    "capabilities"
                                },
                                eye_run = new[]
                                {
                                    "run"
                                },
                                eye_change = Array.Empty<string>(),
                                eye_interact = Array.Empty<string>(),
                                eye_external = Array.Empty<string>()
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
                            message = $"Unknown Eye operation for {GetFacadeName(effectClass)}: {op}",
                            retryable = false
                        }
                    };
            }
        }
        catch (ArgumentException ex)
        {
            return new
            {
                ok = false,
                error = new
                {
                    code = "invalid_argument",
                    message = ex.Message,
                    retryable = true
                }
            };
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
                    retryable = false,
                    type = ex.GetType().FullName
                }
            };
        }
    }

    private static EyeEffectClass? GetEffectClass(string op) => op switch
    {
        "system.status" or "capabilities" => EyeEffectClass.Inspect,
        "run" => EyeEffectClass.Run,
        _ => null
    };

    private static string GetFacadeName(EyeEffectClass effectClass) => effectClass switch
    {
        EyeEffectClass.Inspect => "eye_inspect",
        EyeEffectClass.Run => "eye_run",
        EyeEffectClass.Change => "eye_change",
        EyeEffectClass.Interact => "eye_interact",
        EyeEffectClass.External => "eye_external",
        _ => throw new ArgumentOutOfRangeException(nameof(effectClass), effectClass, null)
    };
}
