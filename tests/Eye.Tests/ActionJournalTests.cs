using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class ActionJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "eye-action-journal-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void DuplicateAction_NeverExecutesTwice_AndRestartRequiresInspection()
    {
        var jobs = Jobs();
        var actions = new ActionJournalStore(jobs);
        var hash = Sha256("same-input");

        var first = actions.Reserve("task_1", "action_1", "file.write", hash);
        Assert.Equal(ActionReservationDisposition.Execute, first.Disposition);
        Assert.True(actions.HasIdempotencyLock("action_1"));

        var duplicate = actions.Reserve("task_1", "action_1", "file.write", hash);
        Assert.Equal(ActionReservationDisposition.InProgress, duplicate.Disposition);

        actions.MarkDispatching("action_1");
        actions.MarkRunning("action_1");

        var reopened = new ActionJournalStore(Jobs());
        Assert.Equal(1, reopened.RecoverAfterHostRestart());
        Assert.Equal(ActionStates.OutcomeUnknown, reopened.GetRequired("action_1").State);

        var afterRestart = reopened.Reserve("task_1", "action_1", "file.write", hash);
        Assert.Equal(ActionReservationDisposition.InspectBeforeReplay, afterRestart.Disposition);

        reopened.MarkVerified("action_1", """{"exists":true}""");
        var completedDuplicate = reopened.Reserve("task_1", "action_1", "file.write", hash);
        Assert.Equal(ActionReservationDisposition.ReturnPrior, completedDuplicate.Disposition);
    }

    [Fact]
    public void RestartBeforeDispatch_IsSafeToReserveAgain()
    {
        var actions = new ActionJournalStore(Jobs());
        var hash = Sha256("input");
        actions.Reserve("task_2", "action_2", "file.write", hash);

        var reopened = new ActionJournalStore(Jobs());
        Assert.Equal(1, reopened.RecoverAfterHostRestart());
        Assert.Equal(ActionStates.Ready, reopened.GetRequired("action_2").State);

        var reservation = reopened.Reserve("task_2", "action_2", "file.write", hash);
        Assert.Equal(ActionReservationDisposition.Execute, reservation.Disposition);
    }

    [Fact]
    public async Task UnknownOutcome_PostconditionPasses_VerifiesWithoutReplay()
    {
        Directory.CreateDirectory(_root);
        var output = Path.Combine(_root, "verified.txt");
        var postcondition = new ActionPostconditionContract(
            FilePostconditionInspector.InspectorKind,
            JsonSerializer.Serialize(new
            {
                path = output,
                min_bytes = 3
            }));

        var actions = new ActionJournalStore(Jobs());
        var hash = Sha256("write-file");
        actions.Reserve("task_3", "action_3", "file.write", hash, postcondition);
        actions.MarkDispatching("action_3");

        await File.WriteAllTextAsync(output, "done");

        var reopened = new ActionJournalStore(Jobs());
        Assert.Equal(1, reopened.RecoverAfterHostRestart());
        Assert.Equal(ActionStates.OutcomeUnknown, reopened.GetRequired("action_3").State);

        var reconciler = new ActionReconciler(
            reopened,
            new PostconditionInspectorRegistry([new FilePostconditionInspector()]));

        var result = await reconciler.InspectUnknownAsync("action_3");

        Assert.Equal(ActionStates.Verified, result.State);
        Assert.True(result.PostconditionSatisfied);
        Assert.False(result.RequiresManualDecision);
        Assert.Contains("sha256", result.EvidenceJson!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownOutcome_FailedPostcondition_StaysUnknownUntilExplicitRetryRelease()
    {
        Directory.CreateDirectory(_root);
        var output = Path.Combine(_root, "missing.txt");
        var postcondition = new ActionPostconditionContract(
            FilePostconditionInspector.InspectorKind,
            JsonSerializer.Serialize(new
            {
                path = output,
                min_bytes = 1
            }));

        var actions = new ActionJournalStore(Jobs());
        var hash = Sha256("missing-file");
        actions.Reserve("task_4", "action_4", "file.write", hash, postcondition);
        actions.MarkDispatching("action_4");

        var reopened = new ActionJournalStore(Jobs());
        Assert.Equal(1, reopened.RecoverAfterHostRestart());
        var reconciler = new ActionReconciler(
            reopened,
            new PostconditionInspectorRegistry([new FilePostconditionInspector()]));

        var result = await reconciler.InspectUnknownAsync("action_4");

        Assert.False(result.PostconditionSatisfied);
        Assert.True(result.RequiresManualDecision);
        Assert.Equal(ActionStates.OutcomeUnknown, reopened.GetRequired("action_4").State);

        reopened.ReleaseForRetryAfterInspection("action_4", result.EvidenceJson!);
        var retried = reopened.Reserve("task_4", "action_4", "file.write", hash, postcondition);
        Assert.Equal(ActionReservationDisposition.Execute, retried.Disposition);
    }

    [Fact]
    public void SameActionId_WithDifferentInputs_IsRejected()
    {
        var actions = new ActionJournalStore(Jobs());
        actions.Reserve("task_5", "action_5", "file.write", Sha256("a"));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            actions.Reserve("task_5", "action_5", "file.write", Sha256("b")));

        Assert.Contains("different inputs", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ledger_DetectsTampering()
    {
        var jobs = Jobs();
        var actions = new ActionJournalStore(jobs);
        actions.Reserve("task_6", "action_6", "file.write", Sha256("input"));
        actions.MarkDispatching("action_6");
        Assert.True(actions.VerifyLedger());

        using (var connection = new SqliteConnection(
            $"Data Source={jobs.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE action_ledger
                SET payload_json = '{"tampered":true}'
                WHERE seq = (SELECT MIN(seq) FROM action_ledger);
                """;
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        Assert.False(actions.VerifyLedger());
    }

    private JobStore Jobs() =>
        new(Path.Combine(_root, "state"), Path.Combine(_root, "spool"));

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}

