using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace StealthEye.Runtime;

public sealed class MissionBlackboardStore
{
    private const int MaxItems = 128;
    private const int MaxRelayItems = 64;
    private const int MaxTextLength = 4000;
    private readonly object _gate = new();
    private readonly string _connectionString;

    public MissionBlackboardStore(JobStore jobs)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = jobs.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
        Initialize();
    }

    public MissionBlackboardRecord Create(string objective)
    {
        objective = RequiredText(objective, nameof(objective));
        var missionId = "mission_" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO mission_blackboards (
                    mission_id, incarnation, revision, objective, facts_json, decisions_json,
                    jobs_json, triggers_json, artifacts_json, questions_json, next_action,
                    relay_json, updated_utc)
                VALUES (
                    $mission_id, 1, 1, $objective, '[]', '[]', '[]', '[]', '[]', '[]', NULL, '[]', $updated_utc);
                """;
            command.Parameters.AddWithValue("$mission_id", missionId);
            command.Parameters.AddWithValue("$objective", objective);
            command.Parameters.AddWithValue("$updated_utc", now.ToString("O"));
            command.ExecuteNonQuery();
        }
        return GetRequired(missionId);
    }

    public MissionBlackboardRecord GetRequired(string missionId)
    {
        if (string.IsNullOrWhiteSpace(missionId))
            throw new ArgumentException("mission_id is required.", nameof(missionId));
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM mission_blackboards WHERE mission_id = $mission_id;";
            command.Parameters.AddWithValue("$mission_id", missionId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                throw new ArgumentException($"Unknown mission_id: {missionId}", nameof(missionId));
            return Read(reader);
        }
    }

    public MissionBlackboardRecord Update(string missionId, MissionBlackboardUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_gate)
        {
            var current = GetRequiredLocked(missionId);
            var objective = update.Objective is null ? current.Objective : RequiredText(update.Objective, nameof(update.Objective));
            var facts = update.Facts is null ? current.Facts : NormalizeItems(update.Facts, nameof(update.Facts));
            var decisions = update.Decisions is null ? current.Decisions : NormalizeItems(update.Decisions, nameof(update.Decisions));
            var jobs = update.Jobs is null ? current.Jobs : NormalizeIds(update.Jobs, nameof(update.Jobs));
            var triggers = update.Triggers is null ? current.Triggers : NormalizeIds(update.Triggers, nameof(update.Triggers));
            var artifacts = update.Artifacts is null ? current.Artifacts : NormalizeIds(update.Artifacts, nameof(update.Artifacts));
            var questions = update.Questions is null ? current.Questions : NormalizeItems(update.Questions, nameof(update.Questions));
            var nextAction = update.ClearNextAction ? null : update.NextAction is null ? current.NextAction : OptionalText(update.NextAction, nameof(update.NextAction));
            var now = DateTimeOffset.UtcNow;

            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE mission_blackboards
                SET revision = revision + 1,
                    objective = $objective,
                    facts_json = $facts,
                    decisions_json = $decisions,
                    jobs_json = $jobs,
                    triggers_json = $triggers,
                    artifacts_json = $artifacts,
                    questions_json = $questions,
                    next_action = $next_action,
                    updated_utc = $updated_utc
                WHERE mission_id = $mission_id;
                """;
            command.Parameters.AddWithValue("$objective", objective);
            command.Parameters.AddWithValue("$facts", JsonSerializer.Serialize(facts));
            command.Parameters.AddWithValue("$decisions", JsonSerializer.Serialize(decisions));
            command.Parameters.AddWithValue("$jobs", JsonSerializer.Serialize(jobs));
            command.Parameters.AddWithValue("$triggers", JsonSerializer.Serialize(triggers));
            command.Parameters.AddWithValue("$artifacts", JsonSerializer.Serialize(artifacts));
            command.Parameters.AddWithValue("$questions", JsonSerializer.Serialize(questions));
            command.Parameters.AddWithValue("$next_action", (object?)nextAction ?? DBNull.Value);
            command.Parameters.AddWithValue("$updated_utc", now.ToString("O"));
            command.Parameters.AddWithValue("$mission_id", missionId);
            if (command.ExecuteNonQuery() != 1)
                throw new ArgumentException($"Unknown mission_id: {missionId}", nameof(missionId));
            return GetRequiredLocked(missionId);
        }
    }

    public MissionBlackboardRecord[] ListRecent(int limit = 10)
    {
        if (limit is < 1 or > 50)
            throw new ArgumentException("limit must be between 1 and 50.", nameof(limit));
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM mission_blackboards ORDER BY updated_utc DESC LIMIT $limit;";
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader();
            var records = new List<MissionBlackboardRecord>();
            while (reader.Read()) records.Add(Read(reader));
            return [.. records];
        }
    }
    internal MissionBlackboardRecord AppendRelay(string missionId, string source, string message)
    {
        source = RequiredText(source, nameof(source));
        message = RequiredText(message, nameof(message));
        lock (_gate)
        {
            var current = GetRequiredLocked(missionId);
            var nextCursor = current.Relay.Length == 0 ? 1 : current.Relay.Max(x => x.Cursor) + 1;
            var relay = current.Relay
                .Append(new MissionRelayEntry(
                    "relay_" + Guid.NewGuid().ToString("N"),
                    nextCursor,
                    source,
                    message,
                    DateTimeOffset.UtcNow))
                .TakeLast(MaxRelayItems)
                .ToArray();
            var now = DateTimeOffset.UtcNow;
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE mission_blackboards
                SET revision = revision + 1,
                    relay_json = $relay,
                    updated_utc = $updated_utc
                WHERE mission_id = $mission_id;
                """;
            command.Parameters.AddWithValue("$relay", JsonSerializer.Serialize(relay));
            command.Parameters.AddWithValue("$updated_utc", now.ToString("O"));
            command.Parameters.AddWithValue("$mission_id", missionId);
            if (command.ExecuteNonQuery() != 1)
                throw new ArgumentException($"Unknown mission_id: {missionId}", nameof(missionId));
            return GetRequiredLocked(missionId);
        }
    }

    private void Initialize()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS mission_blackboards (
                    mission_id TEXT PRIMARY KEY,
                    incarnation INTEGER NOT NULL,
                    revision INTEGER NOT NULL,
                    objective TEXT NOT NULL,
                    facts_json TEXT NOT NULL,
                    decisions_json TEXT NOT NULL,
                    jobs_json TEXT NOT NULL,
                    triggers_json TEXT NOT NULL,
                    artifacts_json TEXT NOT NULL,
                    questions_json TEXT NOT NULL,
                    next_action TEXT NULL,
                    relay_json TEXT NOT NULL,
                    updated_utc TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }
    }

    private MissionBlackboardRecord GetRequiredLocked(string missionId)
    {
        if (string.IsNullOrWhiteSpace(missionId))
            throw new ArgumentException("mission_id is required.", nameof(missionId));
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM mission_blackboards WHERE mission_id = $mission_id;";
        command.Parameters.AddWithValue("$mission_id", missionId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new ArgumentException($"Unknown mission_id: {missionId}", nameof(missionId));
        return Read(reader);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static MissionBlackboardRecord Read(SqliteDataReader reader) => new(
        reader.GetString(reader.GetOrdinal("mission_id")),
        reader.GetInt64(reader.GetOrdinal("incarnation")),
        reader.GetInt64(reader.GetOrdinal("revision")),
        reader.GetString(reader.GetOrdinal("objective")),
        DeserializeStrings(reader.GetString(reader.GetOrdinal("facts_json"))),
        DeserializeStrings(reader.GetString(reader.GetOrdinal("decisions_json"))),
        DeserializeStrings(reader.GetString(reader.GetOrdinal("jobs_json"))),
        DeserializeStrings(reader.GetString(reader.GetOrdinal("triggers_json"))),
        DeserializeStrings(reader.GetString(reader.GetOrdinal("artifacts_json"))),
        DeserializeStrings(reader.GetString(reader.GetOrdinal("questions_json"))),
        NullableString(reader, "next_action"),
        JsonSerializer.Deserialize<MissionRelayEntry[]>(reader.GetString(reader.GetOrdinal("relay_json"))) ?? [],
        DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_utc"))));

    private static string[] DeserializeStrings(string json) => JsonSerializer.Deserialize<string[]>(json) ?? [];

    private static string? NullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static string RequiredText(string value, string name)
    {
        value = value.Trim();
        if (value.Length == 0) throw new ArgumentException($"{name} is required.", name);
        if (value.Length > MaxTextLength) throw new ArgumentException($"{name} exceeds {MaxTextLength} characters.", name);
        return value;
    }

    private static string? OptionalText(string value, string name)
    {
        value = value.Trim();
        if (value.Length == 0) return null;
        if (value.Length > MaxTextLength) throw new ArgumentException($"{name} exceeds {MaxTextLength} characters.", name);
        return value;
    }

    private static string[] NormalizeItems(IEnumerable<string> values, string name) =>
        Normalize(values, name, requireIdShape: false);

    private static string[] NormalizeIds(IEnumerable<string> values, string name) =>
        Normalize(values, name, requireIdShape: true);

    private static string[] Normalize(IEnumerable<string> values, string name, bool requireIdShape)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in values)
        {
            var value = RequiredText(raw, name);
            if (requireIdShape && !value.Contains('_', StringComparison.Ordinal))
                throw new ArgumentException($"{name} contains an invalid stable ID: {value}", name);
            if (seen.Add(value)) result.Add(value);
            if (result.Count > MaxItems)
                throw new ArgumentException($"{name} cannot contain more than {MaxItems} items.", name);
        }
        return [.. result];
    }
}