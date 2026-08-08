using System.Text.Json;
using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class JobDispatcherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eye-dispatcher-tests-" + Guid.NewGuid().ToString("N"));
    private readonly EyeDispatcher _dispatcher;

    public JobDispatcherTests()
    {
        var store = new JobStore(Path.Combine(_root, "state"), Path.Combine(_root, "spool"));
        _dispatcher = new EyeDispatcher(new ProcessRunner(), new JobManager(store, new ProcessRunner()));
    }

    [Fact]
    public async Task Dispatcher_StartWaitReadAndResult_UseV2Envelope()
    {
        var started = Element(await _dispatcher.ExecuteAsync(
            EyeEffectClass.Run,
            "job.start",
            JsonSerializer.SerializeToElement(new RunRequest
            {
                Context = "system",
                FileName = "cmd.exe",
                Arguments = ["/c", "echo dispatcher-ok"],
                TimeoutMs = 10000
            })));

        Assert.True(started.GetProperty("ok").GetBoolean());
        Assert.Equal("job.start", started.GetProperty("operation").GetString());
        var jobId = started.GetProperty("result").GetProperty("job_id").GetString()!;

        var waited = Element(await _dispatcher.ExecuteAsync(
            EyeEffectClass.Inspect,
            "job.wait",
            JsonSerializer.SerializeToElement(new JobWaitArgs(jobId, 10000))));
        Assert.Equal("job.wait", waited.GetProperty("operation").GetString());
        Assert.False(waited.GetProperty("result").GetProperty("wait_timed_out").GetBoolean());
        Assert.Equal(JobStates.Completed, waited.GetProperty("result").GetProperty("job").GetProperty("state").GetString());

        var read = Element(await _dispatcher.ExecuteAsync(
            EyeEffectClass.Inspect,
            "job.read",
            JsonSerializer.SerializeToElement(new JobReadArgs(jobId))));
        Assert.Contains("dispatcher-ok", read.GetProperty("result").GetProperty("text").GetString(), StringComparison.OrdinalIgnoreCase);

        var result = Element(await _dispatcher.ExecuteAsync(
            EyeEffectClass.Inspect,
            "job.result",
            JsonSerializer.SerializeToElement(new JobIdArgs(jobId))));
        Assert.Equal(JobStates.Completed, result.GetProperty("result").GetProperty("state").GetString());
    }

    [Fact]
    public async Task Dispatcher_EnforcesFacadeAndRunSchemaBounds()
    {
        var wrongTool = Element(await _dispatcher.ExecuteAsync(
            EyeEffectClass.Run,
            "job.status",
            JsonSerializer.SerializeToElement(new JobIdArgs("job_missing"))));
        Assert.False(wrongTool.GetProperty("ok").GetBoolean());
        Assert.Equal("wrong_tool", wrongTool.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("eye_inspect", wrongTool.GetProperty("error").GetProperty("expected").GetProperty("tool").GetString());

        var invalid = Element(await _dispatcher.ExecuteAsync(
            EyeEffectClass.Run,
            "run",
            JsonSerializer.SerializeToElement(new RunRequest { FileName = "cmd.exe", TimeoutMs = 0 })));
        Assert.False(invalid.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", invalid.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Dispatcher_ResultRequiresTerminalJob_AndCancelEndsIt()
    {
        var started = Element(await _dispatcher.ExecuteAsync(
            EyeEffectClass.Run,
            "job.start",
            JsonSerializer.SerializeToElement(new RunRequest
            {
                Context = "system",
                FileName = "powershell.exe",
                Arguments = ["-NoProfile", "-Command", "Start-Sleep 30"],
                TimeoutMs = 60000
            })));
        var jobId = started.GetProperty("result").GetProperty("job_id").GetString()!;

        var earlyResult = Element(await _dispatcher.ExecuteAsync(
            EyeEffectClass.Inspect,
            "job.result",
            JsonSerializer.SerializeToElement(new JobIdArgs(jobId))));
        Assert.False(earlyResult.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", earlyResult.GetProperty("error").GetProperty("code").GetString());

        var cancelled = Element(await _dispatcher.ExecuteAsync(
            EyeEffectClass.Run,
            "job.cancel",
            JsonSerializer.SerializeToElement(new JobIdArgs(jobId))));
        Assert.Equal(JobStates.Cancelled, cancelled.GetProperty("result").GetProperty("state").GetString());
    }

    private static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
