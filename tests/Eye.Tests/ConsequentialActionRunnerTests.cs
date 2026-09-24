using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class ConsequentialActionRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "eye-action-runner-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Runner_ExecutesOnce_Verifies_AndDuplicateReturnsPrior()
    {
        Directory.CreateDirectory(_root);
        var output = Path.Combine(_root, "output.txt");
        var actions = Actions();
        var runner = Runner(actions);
        var request = Request("action_once", output);
        var executions = 0;

        var first = await runner.ExecuteAsync(request, async _ =>
        {
            executions++;
            await File.WriteAllTextAsync(output, "verified");
            return new { wrote = true };
        });

        var second = await runner.ExecuteAsync(request, _ =>
        {
            executions++;
            return Task.FromResult<object?>(new { should_not_run = true });
        });

        Assert.Equal(1, executions);
        Assert.Equal(ActionStates.Verified, first.Action.State);
        Assert.True(first.PostconditionSatisfied);
        Assert.Equal(ActionReservationDisposition.ReturnPrior, second.Disposition);
        Assert.False(second.ExecutorInvoked);
    }

    [Fact]
    public async Task Runner_ExceptionAfterSideEffect_PostconditionWinsAndVerifies()
    {
        Directory.CreateDirectory(_root);
        var output = Path.Combine(_root, "side-effect.txt");
        var actions = Actions();
        var runner = Runner(actions);

        var result = await runner.ExecuteAsync(
            Request("action_exception_success", output),
            async _ =>
            {
                await File.WriteAllTextAsync(output, "completed-before-error");
                throw new InvalidOperationException("transport disappeared after write");
            });

        Assert.Equal(ActionStates.Verified, result.Action.State);
        Assert.True(result.PostconditionSatisfied);
        Assert.NotNull(result.ExecutionError);
    }

    [Fact]
    public async Task Runner_ExceptionWithoutSatisfiedPostcondition_RemainsUnknownAndCannotReplay()
    {
        Directory.CreateDirectory(_root);
        var output = Path.Combine(_root, "missing.txt");
        var actions = Actions();
        var runner = Runner(actions);
        var request = Request("action_exception_unknown", output);
        var executions = 0;

        var first = await runner.ExecuteAsync(request, _ =>
        {
            executions++;
            throw new InvalidOperationException("unknown delivery outcome");
        });
        var duplicate = await runner.ExecuteAsync(request, _ =>
        {
            executions++;
            return Task.FromResult<object?>(new { should_not_run = true });
        });

        Assert.Equal(1, executions);
        Assert.Equal(ActionStates.OutcomeUnknown, first.Action.State);
        Assert.Equal(ActionReservationDisposition.InspectBeforeReplay, duplicate.Disposition);
        Assert.False(duplicate.ExecutorInvoked);
    }

    private ActionJournalStore Actions() =>
        new(Jobs());

    private ConsequentialActionRunner Runner(ActionJournalStore actions) =>
        new(
            actions,
            new PostconditionInspectorRegistry([new FilePostconditionInspector()]));

    private ConsequentialActionRequest Request(string actionId, string path) =>
        new(
            "task_runner",
            actionId,
            "file.write",
            Sha256(actionId),
            new ActionPostconditionContract(
                FilePostconditionInspector.InspectorKind,
                JsonSerializer.Serialize(new { path, min_bytes = 1 })));

    private JobStore Jobs() =>
        new(Path.Combine(_root, "state"), Path.Combine(_root, "spool", "jobs"));

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
