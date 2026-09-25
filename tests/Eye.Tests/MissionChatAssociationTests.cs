using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class MissionChatAssociationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "eye-mission-chats-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void AssociationsAreDurableButRelayDoesNotDependOnChatAvailability()
    {
        var jobs = Jobs();
        var blackboard = new MissionBlackboardStore(jobs);
        var mission = blackboard.Create("Coordinate available and closed chats.");
        var chats = new MissionChatAssociationStore(jobs);
        var relay = new RelayService(blackboard);

        var primary = chats.Associate(mission.MissionId, "chat://primary", "builder");
        var secondary = chats.Associate(mission.MissionId, "chat://secondary", "reviewer", available: false);
        var message = relay.Send(mission.MissionId, "chat://primary", "Continue even if the reviewer is closed.");

        Assert.True(primary.Available);
        Assert.False(secondary.Available);
        Assert.Equal("reviewer", secondary.Role);

        var reopenedJobs = new JobStore(jobs.StateRoot, jobs.SpoolRoot);
        var reopenedChats = new MissionChatAssociationStore(reopenedJobs);
        var reopenedRelay = new RelayService(new MissionBlackboardStore(reopenedJobs));

        var associations = reopenedChats.List(mission.MissionId);
        Assert.Equal(2, associations.Length);
        Assert.Contains(associations, x => x.ChatRef == "chat://secondary" && !x.Available);
        Assert.Contains(reopenedRelay.Read(mission.MissionId, 0).Messages, x => x.RelayId == message.RelayId);

        Assert.True(reopenedChats.Remove(mission.MissionId, "chat://secondary"));
        Assert.Single(reopenedChats.List(mission.MissionId));
        Assert.Contains(reopenedRelay.Read(mission.MissionId, 0).Messages, x => x.RelayId == message.RelayId);
    }

    private JobStore Jobs() => new(
        Path.Combine(_root, "state"),
        Path.Combine(_root, "spool", "jobs"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}