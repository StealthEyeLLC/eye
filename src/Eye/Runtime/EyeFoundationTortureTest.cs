using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace StealthEye.Runtime;

public sealed record EyeTortureCase(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("detail")] string Detail);

public sealed record EyeTortureReport(
    [property: JsonPropertyName("captured_at")] DateTimeOffset CapturedAt,
    [property: JsonPropertyName("overall")] string Overall,
    [property: JsonPropertyName("cases")] EyeTortureCase[] Cases);

public static class EyeFoundationTortureTest
{
    public static async Task<EyeTortureReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "stealtheye-torture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var results = new List<EyeTortureCase>();

        try
        {
            await RunCaseAsync(
                results,
                "duplicate_action_suppression",
                () => DuplicateActionSuppressionAsync(root, cancellationToken));
            await RunCaseAsync(
                results,
                "restart_after_dispatch_reconciles_without_replay",
                () => RestartAfterDispatchAsync(root, cancellationToken));
            await RunCaseAsync(
                results,
                "restart_without_postcondition_remains_unknown",
                () => RestartWithoutEffectAsync(root, cancellationToken));
            await RunCaseAsync(
                results,
                "reserved_before_dispatch_is_safe_to_retry",
                () => ReservedBeforeDispatchAsync(root));
            await RunCaseAsync(
                results,
                "tamper_evident_ledger_detects_mutation",
                () => LedgerTamperAsync(root));
        }
        finally
        {
            TryDeleteTree(root);
        }

        var overall = results.Any(x => x.Status == DiagnosticStates.Fail)
            ? DiagnosticStates.Fail
            : DiagnosticStates.Pass;
        return new EyeTortureReport(DateTimeOffset.UtcNow, overall, [.. results]);
    }

    private static async Task DuplicateActionSuppressionAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var scope = Path.Combine(root, "duplicate");
        var jobs = Jobs(scope);
        var actions = new ActionJournalStore(jobs);
        var runner = Runner(actions);
        var output = Path.Combine(scope, "output.txt");
        var request = Request("action_duplicate", output);
        var executions = 0;

        var first = await runner.ExecuteAsync(request, async token =>
        {
            executions++;
            Directory.CreateDirectory(scope);
            await File.WriteAllTextAsync(output, "once", token);
            return new { write = "once" };
        }, cancellationToken);
        var duplicate = await runner.ExecuteAsync(request, _ =>
        {
            executions++;
            return Task.FromResult<object?>(new { invalid_second_execution = true });
        }, cancellationToken);

        Require(first.Action.State == ActionStates.Verified, "first execution did not verify");
        Require(executions == 1, $"executor ran {executions} times");
        Require(
            duplicate.Disposition == ActionReservationDisposition.ReturnPrior,
            $"duplicate disposition was {duplicate.Disposition}");
        Require(!duplicate.ExecutorInvoked, "duplicate executor was invoked");
    }

    private static async Task RestartAfterDispatchAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var scope = Path.Combine(root, "restart-success");
        Directory.CreateDirectory(scope);
        var output = Path.Combine(scope, "side-effect.txt");
        var jobs = Jobs(scope);
        var actions = new ActionJournalStore(jobs);
        var request = Request("action_restart_success", output);

        actions.Reserve(
            request.TaskId,
            request.ActionId,
            request.Capability,
            request.InputSha256,
            request.Postcondition);
        actions.MarkDispatching(request.ActionId);

        // Simulate the external side effect completing just before the host disappears.
        await File.WriteAllTextAsync(output, "completed", cancellationToken);

        var reopened = new ActionJournalStore(Jobs(scope));
        Require(reopened.RecoverAfterHostRestart() == 1, "restart did not recover one action");
        Require(
            reopened.GetRequired(request.ActionId).State == ActionStates.OutcomeUnknown,
            "dispatched action did not become outcome_unknown");

        var reconciler = new ActionReconciler(
            reopened,
            new PostconditionInspectorRegistry([new FilePostconditionInspector()]));
        var reconciled = await reconciler.InspectUnknownAsync(
            request.ActionId,
            cancellationToken);
        Require(
            reconciled.State == ActionStates.Verified,
            "postcondition did not verify completed side effect");
        Require(
            reconciled.PostconditionSatisfied == true,
            "postcondition result was not true");
    }

    private static async Task RestartWithoutEffectAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var scope = Path.Combine(root, "restart-missing");
        Directory.CreateDirectory(scope);
        var output = Path.Combine(scope, "missing.txt");
        var jobs = Jobs(scope);
        var actions = new ActionJournalStore(jobs);
        var request = Request("action_restart_missing", output);

        actions.Reserve(
            request.TaskId,
            request.ActionId,
            request.Capability,
            request.InputSha256,
            request.Postcondition);
        actions.MarkDispatching(request.ActionId);

        var reopened = new ActionJournalStore(Jobs(scope));
        reopened.RecoverAfterHostRestart();
        var reconciler = new ActionReconciler(
            reopened,
            new PostconditionInspectorRegistry([new FilePostconditionInspector()]));
        var reconciled = await reconciler.InspectUnknownAsync(
            request.ActionId,
            cancellationToken);

        Require(
            reconciled.PostconditionSatisfied == false,
            "missing side effect unexpectedly passed postcondition");
        Require(
            reconciled.RequiresManualDecision,
            "missing side effect did not require an explicit retry decision");
        Require(
            reopened.GetRequired(request.ActionId).State == ActionStates.OutcomeUnknown,
            "failed inspection incorrectly changed outcome_unknown state");

        var duplicate = reopened.Reserve(
            request.TaskId,
            request.ActionId,
            request.Capability,
            request.InputSha256,
            request.Postcondition);
        Require(
            duplicate.Disposition == ActionReservationDisposition.InspectBeforeReplay,
            "unknown action became replayable without explicit release");
    }

    private static Task ReservedBeforeDispatchAsync(string root)
    {
        var scope = Path.Combine(root, "reserved");
        var jobs = Jobs(scope);
        var actions = new ActionJournalStore(jobs);
        var request = Request("action_reserved", Path.Combine(scope, "unused.txt"));
        actions.Reserve(
            request.TaskId,
            request.ActionId,
            request.Capability,
            request.InputSha256,
            request.Postcondition);

        var reopened = new ActionJournalStore(Jobs(scope));
        reopened.RecoverAfterHostRestart();
        Require(
            reopened.GetRequired(request.ActionId).State == ActionStates.Ready,
            "never-dispatched action did not recover to ready");
        var reservation = reopened.Reserve(
            request.TaskId,
            request.ActionId,
            request.Capability,
            request.InputSha256,
            request.Postcondition);
        Require(
            reservation.Disposition == ActionReservationDisposition.Execute,
            "never-dispatched action was not safely reservable");

        return Task.CompletedTask;
    }

    private static Task LedgerTamperAsync(string root)
    {
        var scope = Path.Combine(root, "tamper");
        var jobs = Jobs(scope);
        var actions = new ActionJournalStore(jobs);
        var request = Request("action_tamper", Path.Combine(scope, "unused.txt"));
        actions.Reserve(
            request.TaskId,
            request.ActionId,
            request.Capability,
            request.InputSha256,
            request.Postcondition);
        Require(actions.VerifyLedger(), "fresh ledger did not verify");

        using (var connection = new SqliteConnection(
            $"Data Source={jobs.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE action_ledger
                SET payload_json = '{"mutated":true}'
                WHERE seq = (SELECT MIN(seq) FROM action_ledger);
                """;
            Require(command.ExecuteNonQuery() == 1, "failed to mutate disposable ledger");
        }

        Require(!actions.VerifyLedger(), "ledger mutation was not detected");
        return Task.CompletedTask;
    }

    private static ConsequentialActionRunner Runner(ActionJournalStore actions) =>
        new(
            actions,
            new PostconditionInspectorRegistry([new FilePostconditionInspector()]));

    private static ConsequentialActionRequest Request(string actionId, string output) =>
        new(
            "task_torture",
            actionId,
            "test.file.write",
            Sha256(actionId),
            new ActionPostconditionContract(
                FilePostconditionInspector.InspectorKind,
                JsonSerializer.Serialize(new { path = output, min_bytes = 1 })));

    private static JobStore Jobs(string scope) =>
        new(
            Path.Combine(scope, "state"),
            Path.Combine(scope, "spool", "jobs"));

    private static string Sha256(string value) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static async Task RunCaseAsync(
        List<EyeTortureCase> results,
        string name,
        Func<Task> action)
    {
        try
        {
            await action();
            results.Add(new EyeTortureCase(
                name,
                DiagnosticStates.Pass,
                "Postconditions satisfied."));
        }
        catch (Exception ex)
        {
            results.Add(new EyeTortureCase(
                name,
                DiagnosticStates.Fail,
                ex.Message));
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void TryDeleteTree(string root)
    {
        if (!Directory.Exists(root))
            return;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                Directory.Delete(root, recursive: true);
                return;
            }
            catch when (attempt < 9)
            {
                Thread.Sleep(50);
            }
        }
    }
}
