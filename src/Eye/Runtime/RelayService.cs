using System.Text.Json.Serialization;

namespace StealthEye.Runtime;

public sealed record RelayReadResult(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("messages")] MissionRelayEntry[] Messages,
    [property: JsonPropertyName("next_cursor")] long NextCursor,
    [property: JsonPropertyName("latest_cursor")] long LatestCursor,
    [property: JsonPropertyName("gap")] bool Gap);

public sealed class RelayService(MissionBlackboardStore blackboard)
{
    private const int MaxPageItems = 64;

    public MissionRelayEntry Send(string missionId, string source, string message)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("source is required.", nameof(source));
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("message is required.", nameof(message));

        var updated = blackboard.AppendRelay(missionId, source, message);
        return updated.Relay[^1];
    }

    public RelayReadResult Read(string missionId, long afterCursor, int maxItems = MaxPageItems)
    {
        if (afterCursor < 0)
            throw new ArgumentException("after_cursor cannot be negative.", nameof(afterCursor));
        if (maxItems is < 1 or > MaxPageItems)
            throw new ArgumentException($"max_items must be between 1 and {MaxPageItems}.", nameof(maxItems));

        var state = blackboard.GetRequired(missionId);
        var latestCursor = state.Relay.Length == 0 ? 0 : state.Relay[^1].Cursor;
        var gap = state.Relay.Length > 0 && state.Relay[0].Cursor > afterCursor + 1;
        var messages = state.Relay
            .Where(x => x.Cursor > afterCursor)
            .Take(maxItems)
            .ToArray();
        var nextCursor = messages.Length == 0 ? afterCursor : messages[^1].Cursor;

        return new RelayReadResult(
            missionId,
            messages,
            nextCursor,
            latestCursor,
            gap);
    }
}
