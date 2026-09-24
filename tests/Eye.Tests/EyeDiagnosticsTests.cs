using System.Text.Json;
using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class EyeDiagnosticsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "eye-diagnostics-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InventoryAndDoctor_ReportCanonicalFoundationState()
    {
        var jobs = Jobs();
        var actions = new ActionJournalStore(jobs);
        var artifacts = new ArtifactStore(jobs);
        var contract = EyeContractCatalog.Load();
        var diagnostics = new EyeDiagnostics(jobs, actions, artifacts, contract);

        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "artifact.txt");
        await File.WriteAllTextAsync(source, "verified artifact");
        await artifacts.ImportFileAsync(
            source,
            "text",
            "text/plain",
            "artifact.txt",
            "diagnostics-test");

        var inventory = diagnostics.Inventory();
        Assert.Equal("StealthEye", inventory.Product);
        Assert.Equal(
            ["eye_inspect", "eye_run", "eye_change", "eye_interact", "eye_external", "eye_live"],
            inventory.PublicTools);
        Assert.True(inventory.ActionLedgerValid);
        Assert.Equal(1, inventory.ArtifactCount);

        var doctor = await diagnostics.DoctorAsync();
        Assert.DoesNotContain(doctor.Checks, x => x.Status == DiagnosticStates.Fail);
        Assert.Contains(doctor.Checks, x =>
            x.Name == "public_contract" && x.Status == DiagnosticStates.Pass);
        Assert.Contains(doctor.Checks, x =>
            x.Name == "sqlite_quick_check" && x.Status == DiagnosticStates.Pass);
        Assert.Contains(doctor.Checks, x =>
            x.Name == "sqlite_wal" && x.Status == DiagnosticStates.Pass);
        Assert.Contains(doctor.Checks, x =>
            x.Name == "action_ledger_integrity" && x.Status == DiagnosticStates.Pass);
        Assert.Contains(doctor.Checks, x =>
            x.Name == "recent_artifact_integrity" && x.Status == DiagnosticStates.Pass);
    }

    [Fact]
    public async Task Doctor_WarnsOnOutcomeUnknown_WithoutMutatingIt()
    {
        var jobs = Jobs();
        var actions = new ActionJournalStore(jobs);
        var hash = new string('a', 64);
        actions.Reserve(
            "task_doctor",
            "action_doctor",
            "test.capability",
            hash,
            new ActionPostconditionContract(
                FilePostconditionInspector.InspectorKind,
                JsonSerializer.Serialize(new { path = Path.Combine(_root, "missing"), min_bytes = 1 })));
        actions.MarkDispatching("action_doctor");
        actions.RecoverAfterHostRestart();

        var diagnostics = new EyeDiagnostics(
            jobs,
            actions,
            new ArtifactStore(jobs),
            EyeContractCatalog.Load());

        var doctor = await diagnostics.DoctorAsync();
        Assert.Equal(ActionStates.OutcomeUnknown, actions.GetRequired("action_doctor").State);
        Assert.Contains(doctor.Checks, x =>
            x.Name == "outcome_unknown_actions" && x.Status == DiagnosticStates.Warn);
    }

    private JobStore Jobs() =>
        new(Path.Combine(_root, "state"), Path.Combine(_root, "spool", "jobs"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
