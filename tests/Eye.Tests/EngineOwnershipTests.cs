using System.Diagnostics;
using System.Text;
using System.Text.Json;
using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

[Collection("Interactive desktop")]
public sealed class EngineOwnershipTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "eye-engine-ownership-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task HostOwnedState_SurvivesEngineReplacementAndCrashRollback()
    {
        var state = Path.Combine(_root, "state");
        var engines = Path.Combine(_root, "engines");
        var jobState = Path.Combine(_root, "jobs-state");
        var spool = Path.Combine(_root, "jobs");
        Stage(engines, "A");
        Stage(engines, "B");

        var jobs = new JobStore(jobState, spool);
        var manager = new JobManager(jobs, new ProcessRunner());
        var artifacts = new ArtifactStore(jobs);
        var blackboard = new MissionBlackboardStore(jobs);
        var triggers = new TriggerStore(jobs);

        var job = manager.Start(new RunRequest
        {
            Context = "system",
            FileName = "cmd.exe",
            Arguments = ["/d", "/c", "echo host-owned-job"],
            TimeoutMs = 10_000
        });
        var completed = await manager.WaitAsync(job.JobId, 10_000);
        Assert.Equal(JobStates.Completed, completed.Job.State);

        var terminal = manager.Start(
            new RunRequest
            {
                Context = "system",
                FileName = "cmd.exe",
                Arguments = [],
                TimeoutMs = 60_000
            },
            terminal: true,
            columns: 100,
            rows: 30);

        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "host-owned-artifact.txt");
        await File.WriteAllTextAsync(source, "host-owned-artifact", new UTF8Encoding(false));
        var artifact = await artifacts.ImportFileAsync(
            source, "text", "text/plain", "host-owned-artifact.txt", "phase4-ownership");

        var mission = blackboard.Create("Preserve host-owned state across engine replacement.");
        var trigger = triggers.CreateTime(DateTimeOffset.UtcNow.AddHours(1));

        await using var supervisor = new EngineSupervisor(state, engines);
        await supervisor.ActivateAsync("A");
        var b = await supervisor.ActivateAsync("B");
        var firstB = b.ProcessId!.Value;

        await KillAsync(firstB);
        var restartedB = await WaitForStatusAsync(
            supervisor,
            x => x.State == "healthy" &&
                 x.ActiveVersion == "B" &&
                 x.ProcessId is not null &&
                 x.ProcessId != firstB);

        await KillAsync(restartedB.ProcessId!.Value);
        var rolledBack = await WaitForStatusAsync(
            supervisor,
            x => x.State == "healthy" &&
                 x.ActiveVersion == "A" &&
                 x.PreviousVersion == "B");
        Assert.Contains("rolled back", rolledBack.LastError!, StringComparison.OrdinalIgnoreCase);

        await manager.WriteAsync(terminal.JobId, "echo engine-survived-terminal\r\n");
        await Task.Delay(250);
        var terminalRead = await manager.ReadAsync(terminal.JobId, "stdout", 0, 64 * 1024);
        Assert.Contains("engine-survived-terminal", terminalRead.Text, StringComparison.OrdinalIgnoreCase);
        var terminalCancelled = await manager.CancelAsync(terminal.JobId);
        Assert.Equal(JobStates.Cancelled, terminalCancelled.State);

        var reopenedJobs = new JobStore(jobState, spool);
        var reopenedArtifact = new ArtifactStore(reopenedJobs).Info(artifact.ArtifactId);
        var reopenedMission = new MissionBlackboardStore(reopenedJobs).GetRequired(mission.MissionId);
        var reopenedTrigger = new TriggerStore(reopenedJobs).GetRequired(trigger.TriggerId);

        Assert.Equal(JobStates.Completed, reopenedJobs.GetRequired(job.JobId).State);
        Assert.Contains("host-owned-job", await File.ReadAllTextAsync(completed.Job.StdoutPath), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(artifact.Sha256, reopenedArtifact.Sha256);
        Assert.Equal(mission.Objective, reopenedMission.Objective);
        Assert.Equal(TriggerStates.Pending, reopenedTrigger.State);
        Assert.Equal(trigger.TriggerId, reopenedTrigger.TriggerId);
    }

    [Fact]
    public async Task DegradedMode_RetainsRawRepairArtifactStateAndRollback()
    {
        var state = Path.Combine(_root, "degraded-state");
        var engines = Path.Combine(_root, "degraded-engines");
        var jobState = Path.Combine(_root, "degraded-jobs-state");
        var spool = Path.Combine(_root, "degraded-jobs");
        Stage(engines, "A");
        Directory.CreateDirectory(state);
        await File.WriteAllTextAsync(
            Path.Combine(state, "engine-state.json"),
            JsonSerializer.Serialize(new EngineSelectionState("missing", "A")));

        var jobs = new JobStore(jobState, spool);
        var manager = new JobManager(jobs, new ProcessRunner());
        var artifacts = new ArtifactStore(jobs);
        var blackboard = new MissionBlackboardStore(jobs);
        var triggers = new TriggerStore(jobs);

        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "degraded-artifact.txt");
        await File.WriteAllTextAsync(source, "degraded-artifact", new UTF8Encoding(false));
        var artifact = await artifacts.ImportFileAsync(
            source, "text", "text/plain", "degraded-artifact.txt", "phase4-degraded");
        var mission = blackboard.Create("Remain useful without a working engine.");
        var trigger = triggers.CreateTime(DateTimeOffset.UtcNow.AddHours(1));

        await using var supervisor = new EngineSupervisor(state, engines);
        var unavailable = await supervisor.InitializeAsync();
        Assert.Equal("unavailable", unavailable.State);
        Assert.Equal("missing", unavailable.ActiveVersion);

        var dispatcher = new EyeDispatcher(manager, artifacts, supervisor);
        var repair = JsonSerializer.SerializeToElement(await dispatcher.ExecuteAsync(
            EyeEffectClass.Run,
            "run",
            JsonSerializer.SerializeToElement(new RunRequest
            {
                Context = "system",
                FileName = "cmd.exe",
                Arguments = ["/d", "/c", "echo degraded-repair-ok"],
                TimeoutMs = 10_000
            })));
        Assert.True(repair.GetProperty("ok").GetBoolean(), repair.ToString());
        Assert.Contains(
            "degraded-repair-ok",
            repair.GetProperty("result").GetProperty("stdout").GetString(),
            StringComparison.OrdinalIgnoreCase);

        await AssertRunEventuallyContainsAsync(
            dispatcher,
            manager,
            new RunRequest
            {
                Context = "user",
                FileName = "cmd.exe",
                Arguments = ["/d", "/c", "echo degraded-user-ok"],
                TimeoutMs = 20_000
            },
            "degraded-user-ok");

        await AssertRunEventuallyContainsAsync(
            dispatcher,
            manager,
            new RunRequest
            {
                Context = "wsl",
                FileName = "sh",
                Arguments = ["-lc", "printf degraded-wsl-ok"],
                WorkingDirectory = "/tmp",
                TimeoutMs = 20_000
            },
            "degraded-wsl-ok");

        var info = JsonSerializer.SerializeToElement(await dispatcher.ExecuteAsync(
            EyeEffectClass.Inspect,
            "artifact.info",
            JsonSerializer.SerializeToElement(new ArtifactIdArgs(artifact.ArtifactId))));
        Assert.True(info.GetProperty("ok").GetBoolean(), info.ToString());
        Assert.Equal(artifact.Sha256, info.GetProperty("result").GetProperty("sha256").GetString());

        Assert.Equal(mission.Objective, blackboard.GetRequired(mission.MissionId).Objective);
        Assert.Equal(TriggerStates.Pending, triggers.GetRequired(trigger.TriggerId).State);

        var rollback = JsonSerializer.SerializeToElement(await dispatcher.ExecuteAsync(
            EyeEffectClass.Change,
            "engine.rollback",
            null));
        Assert.True(rollback.GetProperty("ok").GetBoolean(), rollback.ToString());
        Assert.Equal("healthy", rollback.GetProperty("result").GetProperty("state").GetString());
        Assert.Equal("A", rollback.GetProperty("result").GetProperty("active_version").GetString());
    }

    private static async Task AssertRunEventuallyContainsAsync(
        EyeDispatcher dispatcher,
        JobManager manager,
        RunRequest request,
        string expected)
    {
        var envelope = JsonSerializer.SerializeToElement(await dispatcher.ExecuteAsync(
            EyeEffectClass.Run,
            "run",
            JsonSerializer.SerializeToElement(request)));
        Assert.True(envelope.GetProperty("ok").GetBoolean(), envelope.ToString());
        var result = envelope.GetProperty("result");
        if (result.TryGetProperty("stdout", out var stdout))
        {
            Assert.Contains(expected, stdout.GetString(), StringComparison.OrdinalIgnoreCase);
            return;
        }

        var jobId = result.GetProperty("job_id").GetString()!;
        var waited = await manager.WaitAsync(jobId, 30_000);
        Assert.False(waited.WaitTimedOut);
        var read = await manager.ReadAsync(jobId, "stdout", 0, 64 * 1024);
        var errorRead = await manager.ReadAsync(jobId, "stderr", 0, 64 * 1024);
        Assert.True(
            waited.Job.State is JobStates.Completed or JobStates.TimedOut,
            $"state={waited.Job.State}; failure_code={waited.Job.FailureCode}; failure_message={waited.Job.FailureMessage}; stderr={errorRead.Text}");
        Assert.True(
            read.Text.Contains(expected, StringComparison.OrdinalIgnoreCase),
            $"expected={expected}; state={waited.Job.State}; stdout={read.Text}; stderr={errorRead.Text}; failure={waited.Job.FailureMessage}");
    }

    private static async Task KillAsync(int pid)
    {
        using var process = Process.GetProcessById(pid);
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }

    private static async Task<EngineSupervisorStatus> WaitForStatusAsync(
        EngineSupervisor supervisor,
        Func<EngineSupervisorStatus, bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var status = supervisor.Status();
            if (predicate(status))
                return status;
            await Task.Delay(50);
        }

        var final = supervisor.Status();
        Assert.Fail($"Engine supervisor did not reach expected state. Final: {final}");
        return final;
    }

    private static void Stage(string engineRoot, string version)
    {
        var source = EngineOutputDirectory();
        var destination = Path.Combine(engineRoot, version);
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
    }

    private static string EngineOutputDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Eye.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var path = Path.Combine(
            directory!.FullName,
            "src",
            "Eye.Engine",
            "bin",
            "Release",
            "net10.0-windows");
        Assert.True(File.Exists(Path.Combine(path, "eye-engine.exe")));
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
