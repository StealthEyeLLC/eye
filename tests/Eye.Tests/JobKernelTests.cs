using System.Diagnostics;
using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class JobKernelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eye-job-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Job_SpoolsReadsWaitsAndCompletes()
    {
        var store = Store();
        var manager = new JobManager(store, new ProcessRunner());
        var job = manager.Start(new RunRequest
        {
            Context = "system",
            FileName = "cmd.exe",
            Arguments = ["/c", "echo alpha&&echo beta"],
            TimeoutMs = 10000
        });

        var waited = await manager.WaitAsync(job.JobId, 10000);
        Assert.False(waited.WaitTimedOut);
        Assert.Equal(JobStates.Completed, waited.Job.State);
        Assert.Equal(0, waited.Job.ExitCode);

        var first = await manager.ReadAsync(job.JobId, "stdout", 0, 7);
        var second = await manager.ReadAsync(job.JobId, "stdout", first.NextCursor, 1024);
        Assert.True(first.NextCursor > 0);
        Assert.Equal(first.NextCursor, second.Cursor);
        Assert.Contains("alpha", first.Text + second.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("beta", first.Text + second.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(second.Eof);
    }

    [Fact]
    public async Task JobRead_DoesNotSplitUtf8Characters()
    {
        var store = Store();
        var paths = store.AllocatePaths("job_utf8_test");
        store.Create("job_utf8_test", new RunRequest { FileName = "cmd.exe" }, paths);
        await File.WriteAllTextAsync(paths.Stdout, "\u03B1\u03B2", new System.Text.UTF8Encoding(false));
        store.Finish(
            "job_utf8_test",
            JobStates.Completed,
            new ProcessRunResult(1, 0, false, string.Empty, string.Empty, "system", "test", 0));

        var manager = new JobManager(store, new ProcessRunner());
        var first = await manager.ReadAsync("job_utf8_test", "stdout", 0, 1);
        var second = await manager.ReadAsync("job_utf8_test", "stdout", first.NextCursor, 1);
        Assert.Equal("\u03B1", first.Text);
        Assert.Equal("\u03B2", second.Text);
        Assert.Equal(2, first.NextCursor);
        Assert.True(second.Eof);
    }
    [Fact]
    public async Task TerminalJob_WritesResizesAttachesAndCompletes()
    {
        var store = Store();
        var manager = new JobManager(store, new ProcessRunner());
        var job = manager.Start(new RunRequest
        {
            Context = "system",
            FileName = "powershell.exe",
            Arguments = ["-NoLogo", "-NoProfile", "-NoExit"],
            TimeoutMs = 10000
        }, terminal: true, columns: 80, rows: 25);

        Assert.True(job.Terminal);
        var resized = manager.Resize(job.JobId, 110, 35);
        Assert.Equal(110, resized.Columns);
        Assert.Equal(35, resized.Rows);

        var attached = manager.Attach(job.JobId);
        Assert.True(attached.Job.Terminal);
        Assert.True(attached.StdoutCursor >= 0);
        Assert.Equal(0, attached.StderrCursor);

        var written = await manager.WriteAsync(job.JobId, "Write-Output eye-job-terminal\rexit\r");
        Assert.True(written > 0);

        var waited = await manager.WaitAsync(job.JobId, 10000);
        Assert.False(waited.WaitTimedOut);
        Assert.Equal(JobStates.Completed, waited.Job.State);

        var read = await manager.ReadAsync(job.JobId, "stdout", 0, 65536);
        Assert.Contains("eye-job-terminal", read.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(read.Eof);
    }

    [Fact]
    public async Task Job_CancelTerminatesOwnedProcess()
    {
        var store = Store();
        var manager = new JobManager(store, new ProcessRunner());
        var job = manager.Start(new RunRequest
        {
            Context = "system",
            FileName = "powershell.exe",
            Arguments = ["-NoProfile", "-Command", "Start-Sleep 30"],
            TimeoutMs = 0
        });

        JobRecord running;
        do
        {
            await Task.Delay(25);
            running = manager.Status(job.JobId);
        } while (running.State == JobStates.Starting);

        Assert.Equal(JobStates.Running, running.State);
        Assert.NotNull(running.Pid);
        var cancelled = await manager.CancelAsync(job.JobId);
        Assert.Equal(JobStates.Cancelled, cancelled.State);
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(running.Pid!.Value));
    }

    [Fact]
    public void Store_MarksUnrecoverableLiveMetadataInterrupted()
    {
        var store = Store();
        var paths = store.AllocatePaths("job_recovery_test");
        store.Create("job_recovery_test", new RunRequest { FileName = "cmd.exe" }, paths);
        store.MarkRunning("job_recovery_test", 12345, "TEST\\User");

        var reopened = Store();
        var recovered = reopened.GetRequired("job_recovery_test");
        Assert.Equal(JobStates.Interrupted, recovered.State);
        Assert.Equal("host_restarted", recovered.FailureCode);
    }

    private JobStore Store() => new(Path.Combine(_root, "state"), Path.Combine(_root, "spool"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
