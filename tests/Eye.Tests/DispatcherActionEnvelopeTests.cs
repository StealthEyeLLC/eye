using System.Text.Json;
using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class DispatcherActionEnvelopeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "eye-dispatch-action-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DuplicateActionId_ExecutesSideEffectOnce_AndStatusIsVerified()
    {
        Directory.CreateDirectory(_root);
        var marker = Path.Combine(_root, "effect.txt");
        var jobs = new JobStore(
            Path.Combine(_root, "state"),
            Path.Combine(_root, "spool"));
        var processRunner = new ProcessRunner();
        var manager = new JobManager(jobs, processRunner);
        var actions = new ActionJournalStore(jobs);
        var inspectors = new PostconditionInspectorRegistry(
            [new FilePostconditionInspector(), new CommandPostconditionInspector(processRunner)]);
        var actionRunner = new ConsequentialActionRunner(actions, inspectors);
        var reconciler = new ActionReconciler(actions, inspectors);
        var dispatcher = new EyeDispatcher(
            manager,
            new ArtifactStore(jobs),
            actionJournalStore: actions,
            consequentialActionRunner: actionRunner,
            actionReconciler: reconciler);

        var command = $"Add-Content -LiteralPath '{marker.Replace("'", "''", StringComparison.Ordinal)}' -Value 'once'";
        var args = JsonSerializer.SerializeToElement(new RunArgs(
            "powershell.exe",
            "system",
            ["-NoProfile", "-Command", command],
            TimeoutMs: 10_000));
        var envelope = new ActionExecutionEnvelope(
            "task_dispatcher",
            "action_dispatcher_once",
            new ActionPostconditionContract(
                FilePostconditionInspector.InspectorKind,
                JsonSerializer.Serialize(new { path = marker, min_bytes = 1 })));

        var first = JsonSerializer.SerializeToElement(await dispatcher.ExecuteAsync(
            EyeEffectClass.Run,
            "run",
            args,
            envelope));
        var second = JsonSerializer.SerializeToElement(await dispatcher.ExecuteAsync(
            EyeEffectClass.Run,
            "run",
            args,
            envelope));

        Assert.True(first.GetProperty("ok").GetBoolean(), first.ToString());
        Assert.True(second.GetProperty("ok").GetBoolean(), second.ToString());
        Assert.True(File.Exists(marker));
        Assert.Single(await File.ReadAllLinesAsync(marker));

        var status = JsonSerializer.SerializeToElement(await dispatcher.ExecuteAsync(
            EyeEffectClass.Inspect,
            "action.status",
            JsonSerializer.SerializeToElement(new ActionIdArgs("action_dispatcher_once"))));
        Assert.True(status.GetProperty("ok").GetBoolean(), status.ToString());
        Assert.Equal(
            ActionStates.Verified,
            status.GetProperty("result").GetProperty("state").GetString());
        Assert.Equal(
            "task_dispatcher",
            status.GetProperty("result").GetProperty("task_id").GetString());
    }

    [Fact]
    public async Task PartialActionEnvelope_IsRejectedBeforeExecution()
    {
        Directory.CreateDirectory(_root);
        var jobs = new JobStore(
            Path.Combine(_root, "state-partial"),
            Path.Combine(_root, "spool-partial"));
        var processRunner = new ProcessRunner();
        var manager = new JobManager(jobs, processRunner);
        var actions = new ActionJournalStore(jobs);
        var inspectors = new PostconditionInspectorRegistry([new FilePostconditionInspector()]);
        var dispatcher = new EyeDispatcher(
            manager,
            new ArtifactStore(jobs),
            actionJournalStore: actions,
            consequentialActionRunner: new ConsequentialActionRunner(actions, inspectors),
            actionReconciler: new ActionReconciler(actions, inspectors));

        var result = JsonSerializer.SerializeToElement(await dispatcher.ExecuteAsync(
            EyeEffectClass.Run,
            "run",
            JsonSerializer.SerializeToElement(new RunArgs("cmd.exe", Arguments: ["/d", "/c", "echo SHOULD_NOTRUN"])),
            new ActionExecutionEnvelope("task_only", null, null)));

        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", result.GetProperty("error").GetProperty("code").GetString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
