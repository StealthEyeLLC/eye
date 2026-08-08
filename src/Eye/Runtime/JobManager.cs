using System.Collections.Concurrent;
using System.Text;

namespace StealthEye.Runtime;

public sealed class JobManager(JobStore store, ProcessRunner processRunner)
{
    private readonly ConcurrentDictionary<string, ActiveJob> _active = new(StringComparer.Ordinal);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public JobRecord Start(RunRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FileName))
            throw new ArgumentException("file_name is required.", nameof(request));

        var jobId = "job_" + Guid.NewGuid().ToString("N");
        var paths = store.AllocatePaths(jobId);
        var record = store.Create(jobId, request, paths);
        var active = new ActiveJob();
        if (!_active.TryAdd(jobId, active))
            throw new InvalidOperationException("Unable to register new job.");

        _ = RunJobAsync(record, request, active);
        return record;
    }

    public JobRecord Status(string jobId) => store.GetRequired(jobId);

    public async Task<JobWaitResult> WaitAsync(string jobId, int waitMs, CancellationToken cancellationToken = default)
    {
        if (waitMs < 0 || waitMs > 86_400_000)
            throw new ArgumentException("wait_ms must be between 0 and 86400000.", nameof(waitMs));

        var current = store.GetRequired(jobId);
        if (JobStates.IsTerminal(current.State))
            return new JobWaitResult(current, false);

        if (!_active.TryGetValue(jobId, out var active))
            return new JobWaitResult(store.GetRequired(jobId), false);

        if (waitMs == 0)
            return new JobWaitResult(await active.Completion.Task.WaitAsync(cancellationToken), false);

        var delay = Task.Delay(waitMs, cancellationToken);
        var winner = await Task.WhenAny(active.Completion.Task, delay);
        if (winner == active.Completion.Task)
            return new JobWaitResult(await active.Completion.Task, false);

        await delay;
        return new JobWaitResult(store.GetRequired(jobId), true);
    }

    public async Task<JobRecord> CancelAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var current = store.GetRequired(jobId);
        if (JobStates.IsTerminal(current.State))
            return current;

        if (!_active.TryGetValue(jobId, out var active))
            return store.GetRequired(jobId);

        active.Cancellation.Cancel();
        return await active.Completion.Task.WaitAsync(cancellationToken);
    }

    public JobRecord Result(string jobId)
    {
        var record = store.GetRequired(jobId);
        if (!JobStates.IsTerminal(record.State))
            throw new ArgumentException($"Job {jobId} is not complete yet.", nameof(jobId));
        return record;
    }

    public async Task<JobReadResult> ReadAsync(
        string jobId,
        string stream,
        long cursor,
        int maxBytes,
        CancellationToken cancellationToken = default)
    {
        if (cursor < 0)
            throw new ArgumentException("cursor must be non-negative.", nameof(cursor));
        if (maxBytes is < 1 or > 1_048_576)
            throw new ArgumentException("max_bytes must be between 1 and 1048576.", nameof(maxBytes));

        var record = store.GetRequired(jobId);
        var path = stream switch
        {
            "stdout" => record.StdoutPath,
            "stderr" => record.StderrPath,
            _ => throw new ArgumentException("stream must be stdout or stderr.", nameof(stream))
        };

        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);
        var length = file.Length;
        if (cursor > length)
            throw new ArgumentException($"cursor {cursor} exceeds current stream length {length}.", nameof(cursor));

        file.Seek(cursor, SeekOrigin.Begin);
        var target = (int)Math.Min(maxBytes, length - cursor);
        var capacity = (int)Math.Min((long)target + 3, length - cursor);
        var buffer = new byte[capacity];
        var read = target == 0 ? 0 : await file.ReadAsync(buffer.AsMemory(0, target), cancellationToken);

        string text = string.Empty;
        while (true)
        {
            try
            {
                text = StrictUtf8.GetString(buffer, 0, read);
                break;
            }
            catch (DecoderFallbackException) when (read < capacity)
            {
                var extra = await file.ReadAsync(buffer.AsMemory(read, 1), cancellationToken);
                if (extra == 0)
                    break;
                read += extra;
            }
            catch (DecoderFallbackException)
            {
                var latest = store.GetRequired(jobId);
                for (var trim = 1; trim <= Math.Min(3, read); trim++)
                {
                    try
                    {
                        text = StrictUtf8.GetString(buffer, 0, read - trim);
                        read -= trim;
                        goto decoded;
                    }
                    catch (DecoderFallbackException)
                    {
                    }
                }

                if (!JobStates.IsTerminal(latest.State) && cursor + read >= length)
                {
                    text = string.Empty;
                    read = 0;
                    break;
                }

                throw new ArgumentException("cursor must be a next_cursor previously returned by job.read.", nameof(cursor));
            }
        }

decoded:
        var nextCursor = cursor + read;
        var current = store.GetRequired(jobId);
        var eof = JobStates.IsTerminal(current.State) && nextCursor >= new FileInfo(path).Length;
        return new JobReadResult(jobId, stream, cursor, text, nextCursor, eof, current.State);
    }
    public async Task<ProcessRunResult?> TryGetInlineProcessResultAsync(
        JobRecord job,
        long maxOutputBytes,
        CancellationToken cancellationToken = default)
    {
        if (job.State is not (JobStates.Completed or JobStates.TimedOut) ||
            job.Pid is null || job.ExitCode is null || job.EffectiveIdentity is null || job.CompletedAt is null)
            return null;

        var stdoutLength = new FileInfo(job.StdoutPath).Length;
        var stderrLength = new FileInfo(job.StderrPath).Length;
        if (stdoutLength + stderrLength > maxOutputBytes)
            return null;

        var stdout = await File.ReadAllTextAsync(job.StdoutPath, cancellationToken);
        var stderr = await File.ReadAllTextAsync(job.StderrPath, cancellationToken);
        var startedAt = job.StartedAt ?? job.CreatedAt;
        var durationMs = Math.Max(0, (long)(job.CompletedAt.Value - startedAt).TotalMilliseconds);
        return new ProcessRunResult(
            job.Pid.Value,
            job.ExitCode.Value,
            job.TimedOut,
            stdout,
            stderr,
            job.Context,
            job.EffectiveIdentity,
            durationMs);
    }
    private async Task RunJobAsync(JobRecord record, RunRequest request, ActiveJob active)
    {
        try
        {
            await using var stdoutFile = new FileStream(record.StdoutPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);
            await using var stderrFile = new FileStream(record.StderrPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);
            await using var stdout = new StreamWriter(stdoutFile, new UTF8Encoding(false)) { AutoFlush = true };
            await using var stderr = new StreamWriter(stderrFile, new UTF8Encoding(false)) { AutoFlush = true };

            var hooks = new ProcessRunHooks
            {
                CaptureOutput = false,
                Started = (pid, identity) => store.MarkRunning(record.JobId, pid, identity),
                Output = async (channel, text) =>
                {
                    var writer = channel == ProcessOutputChannel.Stdout ? stdout : stderr;
                    await writer.WriteAsync(text);
                    await writer.FlushAsync();
                }
            };

            var result = await processRunner.RunAsync(request, active.Cancellation.Token, hooks);
            var finalState = result.TimedOut ? JobStates.TimedOut : JobStates.Completed;
            Complete(active, store.Finish(record.JobId, finalState, result));
        }
        catch (OperationCanceledException) when (active.Cancellation.IsCancellationRequested)
        {
            Complete(active, store.Finish(record.JobId, JobStates.Cancelled, failureCode: "cancelled", failureMessage: "Job cancelled."));
        }
        catch (Exception ex)
        {
            Complete(active, store.Finish(record.JobId, JobStates.Failed, failureCode: "job_failed", failureMessage: ex.Message));
        }
        finally
        {
            _active.TryRemove(record.JobId, out _);
            active.Cancellation.Dispose();
        }
    }

    private static void Complete(ActiveJob active, JobRecord record) =>
        active.Completion.TrySetResult(record);

    private sealed class ActiveJob
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public TaskCompletionSource<JobRecord> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
