using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class RelayServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eye-relay-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SendReadAndReopen_PreserveDurableCursorHandoffs()
    {
        var jobs = Jobs();
        var blackboard = new MissionBlackboardStore(jobs);
        var mission = blackboard.Create("Coordinate tabs");
        var relay = new RelayService(blackboard);

        var first = relay.Send(mission.MissionId, "tab-a", "phase complete");
        var second = relay.Send(mission.MissionId, "tab-b", "continuing next slice");

        Assert.StartsWith("relay_", first.RelayId, StringComparison.Ordinal);
        Assert.Equal(1, first.Cursor);
        Assert.Equal(2, second.Cursor);
        var unread = relay.Read(mission.MissionId, afterCursor: first.Cursor);
        Assert.False(unread.Gap);
        Assert.Equal(2, unread.LatestCursor);
        Assert.Equal(2, unread.NextCursor);
        Assert.Single(unread.Messages);
        Assert.Equal(second.RelayId, unread.Messages[0].RelayId);

        var reopenedBoard = new MissionBlackboardStore(new JobStore(jobs.StateRoot, jobs.SpoolRoot));
        var reopened = new RelayService(reopenedBoard).Read(mission.MissionId, 0);
        Assert.Equal([first.RelayId, second.RelayId], reopened.Messages.Select(x => x.RelayId).ToArray());
        Assert.Equal(3, reopenedBoard.GetRequired(mission.MissionId).Revision);
    }

    [Fact]
    public void Relay_RemainsCompactAndReportsCursorGap()
    {
        var blackboard = new MissionBlackboardStore(Jobs());
        var mission = blackboard.Create("Bounded relay");
        var relay = new RelayService(blackboard);

        for (var i = 1; i <= 70; i++)
            relay.Send(mission.MissionId, "tab", $"handoff-{i}");

        var state = blackboard.GetRequired(mission.MissionId);
        Assert.Equal(64, state.Relay.Length);
        Assert.Equal(7, state.Relay[0].Cursor);
        Assert.Equal(70, state.Relay[^1].Cursor);
        Assert.Equal(71, state.Revision);

        var read = relay.Read(mission.MissionId, afterCursor: 0, maxItems: 64);
        Assert.True(read.Gap);
        Assert.Equal(64, read.Messages.Length);
        Assert.Equal(70, read.NextCursor);
        Assert.Equal(70, read.LatestCursor);

        var current = relay.Read(mission.MissionId, afterCursor: 70);
        Assert.False(current.Gap);
        Assert.Empty(current.Messages);
        Assert.Equal(70, current.NextCursor);
    }

    [Fact]
    public void Relay_ValidatesCursorAndPageBounds()
    {
        var blackboard = new MissionBlackboardStore(Jobs());
        var mission = blackboard.Create("Bounds");
        var relay = new RelayService(blackboard);
        Assert.Throws<ArgumentException>(() => relay.Read(mission.MissionId, -1));
        Assert.Throws<ArgumentException>(() => relay.Read(mission.MissionId, 0, 0));
        Assert.Throws<ArgumentException>(() => relay.Send(mission.MissionId, "", "message"));
    }

    private JobStore Jobs() => new(
        Path.Combine(_root, "state"),
        Path.Combine(_root, "spool", "jobs"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}