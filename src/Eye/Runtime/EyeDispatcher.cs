using System.Security.Principal;
using System.Text.Json;
using StealthEye.Contract;

namespace StealthEye.Runtime;

public enum EyeEffectClass
{
    Inspect,
    Run,
    Change,
    Interact,
    External
}

public sealed class EyeDispatcher(ProcessRunner processRunner, JobManager jobManager)
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
                return Failure(
                    op,
                    "wrong_tool",
                    $"Operation '{op}' belongs to {GetFacadeName(requiredClass.Value)}, not {GetFacadeName(effectClass)}.",
                    true,
                    new { tool = GetFacadeName(requiredClass.Value) });
            }

            switch (op)
            {
                case "system.status":
                    return Success(op, new SystemStatusResult(
                        "StealthEye",
                        "eye",
                        typeof(EyeDispatcher).Assembly.GetName().Version?.ToString() ?? "unknown",
                        "eye-mcp-v2",
                        Environment.ProcessId,
                        Environment.MachineName,
                        WindowsIdentity.GetCurrent().Name,
                        System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()));

                case "capabilities":
                    return Success(op, new CapabilitiesResult(
                        "eye-mcp-v2",
                        new CapabilityFacades(
                            ["system.status", "capabilities", "job.status", "job.read", "job.wait", "job.result"],
                            ["run", "job.start", "job.cancel"],
                            [],
                            [],
                            [],
                            [])));

                case "run":
                {
                    var request = DeserializeRequired<RunRequest>(op, args);
                    ValidateRunRequest(request);
                    var result = await processRunner.RunAsync(request, cancellationToken);
                    return Success(op, new RunOperationResult(
                        result.Pid,
                        result.ExitCode,
                        result.TimedOut,
                        result.Stdout,
                        result.Stderr,
                        result.Context,
                        result.EffectiveIdentity,
                        result.DurationMs));
                }

                case "job.start":
                {
                    var request = DeserializeRequired<RunRequest>(op, args);
                    ValidateRunRequest(request);
                    var job = jobManager.Start(request);
                    return Success(op, new JobReferenceResult(job.JobId, job.Incarnation, job.State));
                }

                case "job.status":
                {
                    var request = DeserializeRequired<JobIdArgs>(op, args);
                    return Success(op, ToPublic(jobManager.Status(request.JobId)));
                }

                case "job.read":
                {
                    var request = DeserializeRequired<JobReadArgs>(op, args);
                    var read = await jobManager.ReadAsync(
                        request.JobId,
                        request.Stream,
                        request.Cursor,
                        request.MaxBytes,
                        cancellationToken);
                    return Success(op, new JobReadPublicResult(
                        read.JobId,
                        read.Stream,
                        read.Cursor,
                        read.Text,
                        read.NextCursor,
                        read.Eof,
                        read.State));
                }

                case "job.wait":
                {
                    var request = DeserializeRequired<JobWaitArgs>(op, args);
                    var waited = await jobManager.WaitAsync(request.JobId, request.WaitMs, cancellationToken);
                    return Success(op, new JobWaitPublicResult(ToPublic(waited.Job), waited.WaitTimedOut));
                }

                case "job.cancel":
                {
                    var request = DeserializeRequired<JobIdArgs>(op, args);
                    var cancelled = await jobManager.CancelAsync(request.JobId, cancellationToken);
                    return Success(op, new JobCancelResult(cancelled.JobId, cancelled.State));
                }

                case "job.result":
                {
                    var request = DeserializeRequired<JobIdArgs>(op, args);
                    return Success(op, ToPublic(jobManager.Result(request.JobId)));
                }

                default:
                    return Failure(
                        op,
                        "unknown_operation",
                        $"Unknown Eye operation for {GetFacadeName(effectClass)}: {op}",
                        false);
            }
        }
        catch (ArgumentException ex)
        {
            return Failure(op, "invalid_argument", ex.Message, true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(op, "request_cancelled", "The Eye request was cancelled.", true);
        }
        catch (Exception ex)
        {
            return Failure(op, "operation_failed", ex.Message, false);
        }
    }

    private static void ValidateRunRequest(RunRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FileName))
            throw new ArgumentException("file_name is required.");
        if (request.Context is not ("system" or "user" or "wsl"))
            throw new ArgumentException("context must be system, user, or wsl.");
        if (request.TimeoutMs is < 1 or > 86_400_000)
            throw new ArgumentException("timeout_ms must be between 1 and 86400000.");
    }
    private static T DeserializeRequired<T>(string op, JsonElement? args)
    {
        if (args is null || args.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            throw new ArgumentException($"{op} requires args.");
        return args.Value.Deserialize<T>()
            ?? throw new ArgumentException($"Unable to deserialize {op} args.");
    }

    private static JobStatusResult ToPublic(JobRecord job) => new(
        job.JobId,
        job.Incarnation,
        job.State,
        job.Context,
        job.Pid,
        job.EffectiveIdentity,
        job.CreatedAt,
        job.StartedAt,
        job.CompletedAt,
        job.ExitCode,
        job.TimedOut,
        job.FailureCode,
        job.FailureMessage);

    private static EyeSuccess<T> Success<T>(string op, T result) => new(true, op, result);

    private static EyeFailure Failure(
        string op,
        string code,
        string message,
        bool retryable,
        object? expected = null) =>
        new(false, op, new EyeError(code, message, retryable, expected));

    private static EyeEffectClass? GetEffectClass(string op) => op switch
    {
        "system.status" or "capabilities" or "job.status" or "job.read" or "job.wait" or "job.result" => EyeEffectClass.Inspect,
        "run" or "job.start" or "job.cancel" => EyeEffectClass.Run,
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
