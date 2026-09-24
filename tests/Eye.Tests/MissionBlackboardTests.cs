using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class MissionBlackboardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eye-blackboard-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreateUpdateAndReopen_PreserveStableMissionIdentity()
    {
        var jobs = Jobs();
        var store = new MissionBlackboardStore(jobs);
        var created = store.Create("Ship Phase 7 continuity");

        Assert.StartsWith("mission_", created.MissionId, StringComparison.Ordinal);
        Assert.Equal(1, created.Incarnation);
        Assert.Equal(1, created.Revision);
        Assert.Empty(created.Facts);
        Assert.Empty(created.Decisions);
        Assert.Empty(created.Jobs);
        Assert.Empty(created.Triggers);
        Assert.Empty(created.Artifacts);
        Assert.Empty(created.Questions);
        Assert.Empty(created.Relay);
        Assert.Null(created.NextAction);

        var updated = store.Update(created.MissionId, new MissionBlackboardUpdate(
            Facts: ["The stable host owns the Blackboard."],
            Decisions: ["Keep it compact."],
            Jobs: ["job_test"],
            Triggers: ["trigger_test"],
            Artifacts: ["artifact_test"],
            Questions: ["What is next?"],
            NextAction: "Build Relay."));

        Assert.Equal(created.MissionId, updated.MissionId);
        Assert.Equal(created.Incarnation, updated.Incarnation);
        Assert.Equal(2, updated.Revision);
        Assert.Equal(["The stable host owns the Blackboard."], updated.Facts);
        Assert.Equal(["Keep it compact."], updated.Decisions);
        Assert.Equal(["job_test"], updated.Jobs);
        Assert.Equal(["trigger_test"], updated.Triggers);
        Assert.Equal(["artifact_test"], updated.Artifacts);
        Assert.Equal(["What is next?"], updated.Questions);
        Assert.Equal("Build Relay.", updated.NextAction);

        var cleared = store.Update(created.MissionId, new MissionBlackboardUpdate(ClearNextAction: true));
        Assert.Equal(3, cleared.Revision);
        Assert.Null(cleared.NextAction);

        var reopened = new MissionBlackboardStore(new JobStore(jobs.StateRoot, jobs.SpoolRoot)).GetRequired(created.MissionId);
        Assert.Equal(created.MissionId, reopened.MissionId);
        Assert.Equal(1, reopened.Incarnation);
        Assert.Equal(3, reopened.Revision);
        Assert.Equal(updated.Facts, reopened.Facts);
        Assert.Equal(updated.Decisions, reopened.Decisions);
        Assert.Equal(updated.Jobs, reopened.Jobs);
        Assert.Equal(updated.Triggers, reopened.Triggers);
        Assert.Equal(updated.Artifacts, reopened.Artifacts);
        Assert.Equal(updated.Questions, reopened.Questions);
    }

    [Fact]
    public void Update_DeduplicatesCompactArraysWithoutInventingTaxonomy()
    {
        var store = new MissionBlackboardStore(Jobs());
        var created = store.Create("Compact mission");
        var updated = store.Update(created.MissionId, new MissionBlackboardUpdate(
            Facts: ["fact", "fact", "other"],
            Jobs: ["job_a", "job_a", "job_b"]));

        Assert.Equal(["fact", "other"], updated.Facts);
        Assert.Equal(["job_a", "job_b"], updated.Jobs);
    }

    [Fact]
    public void UnknownMission_IsRejected()
    {
        var store = new MissionBlackboardStore(Jobs());
        Assert.Throws<ArgumentException>(() => store.GetRequired("mission_missing"));
        Assert.Throws<ArgumentException>(() => store.Update("mission_missing", new MissionBlackboardUpdate(Objective: "x")));
    }

    private JobStore Jobs() => new(
        Path.Combine(_root, "state"),
        Path.Combine(_root, "spool", "jobs"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}