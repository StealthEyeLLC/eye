using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace StealthEye.Runtime;

public sealed record MissionChatAssociation(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("chat_ref")] string ChatRef,
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

public sealed class MissionChatAssociationStore
{
    private readonly object _gate = new();
    private readonly string _connectionString;

    public MissionChatAssociationStore(JobStore jobs)
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

    public MissionChatAssociation Associate(
        string missionId,
        string chatRef,
        string? role = null,
        bool available = true)
    {
        Validate(missionId, chatRef);
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO mission_chats (mission_id, chat_ref, role, available, updated_utc)
                VALUES ($mission_id, $chat_ref, $role, $available, $updated_utc)
                ON CONFLICT(mission_id, chat_ref) DO UPDATE SET
                    role = excluded.role,
                    available = excluded.available,
                    updated_utc = excluded.updated_utc;
                """;
            command.Parameters.AddWithValue("$mission_id", missionId.Trim());
            command.Parameters.AddWithValue("$chat_ref", chatRef.Trim());
            command.Parameters.AddWithValue("$role", (object?)Normalize(role) ?? DBNull.Value);
            command.Parameters.AddWithValue("$available", available ? 1 : 0);
            command.Parameters.AddWithValue("$updated_utc", now.ToString("O"));
            command.ExecuteNonQuery();
        }

        return new MissionChatAssociation(
            missionId.Trim(),
            chatRef.Trim(),
            Normalize(role),
            available,
            now);
    }

    public bool Remove(string missionId, string chatRef)
    {
        Validate(missionId, chatRef);
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM mission_chats
                WHERE mission_id = $mission_id AND chat_ref = $chat_ref;
                """;
            command.Parameters.AddWithValue("$mission_id", missionId.Trim());
            command.Parameters.AddWithValue("$chat_ref", chatRef.Trim());
            return command.ExecuteNonQuery() > 0;
        }
    }

    public MissionChatAssociation[] List(string missionId)
    {
        if (string.IsNullOrWhiteSpace(missionId))
            throw new ArgumentException("mission_id is required.", nameof(missionId));

        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT mission_id, chat_ref, role, available, updated_utc
                FROM mission_chats
                WHERE mission_id = $mission_id
                ORDER BY updated_utc DESC, chat_ref COLLATE NOCASE;
                """;
            command.Parameters.AddWithValue("$mission_id", missionId.Trim());
            using var reader = command.ExecuteReader();
            var rows = new List<MissionChatAssociation>();
            while (reader.Read())
            {
                rows.Add(new MissionChatAssociation(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetInt64(3) != 0,
                    DateTimeOffset.Parse(reader.GetString(4))));
            }
            return [.. rows];
        }
    }

    public MissionChatAssociation[] ListRecent(int limit = 32)
    {
        if (limit is < 1 or > 128)
            throw new ArgumentException("limit must be between 1 and 128.", nameof(limit));

        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT mission_id, chat_ref, role, available, updated_utc
                FROM mission_chats
                ORDER BY updated_utc DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader();
            var rows = new List<MissionChatAssociation>();
            while (reader.Read())
            {
                rows.Add(new MissionChatAssociation(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetInt64(3) != 0,
                    DateTimeOffset.Parse(reader.GetString(4))));
            }
            return [.. rows];
        }
    }

    private void Initialize()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS mission_chats (
                    mission_id TEXT NOT NULL,
                    chat_ref TEXT NOT NULL,
                    role TEXT NULL,
                    available INTEGER NOT NULL,
                    updated_utc TEXT NOT NULL,
                    PRIMARY KEY (mission_id, chat_ref)
                );
                CREATE INDEX IF NOT EXISTS idx_mission_chats_updated
                ON mission_chats(updated_utc DESC);
                """;
            command.ExecuteNonQuery();
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= 64
            ? trimmed
            : throw new ArgumentException("role may contain at most 64 characters.", nameof(value));
    }

    private static void Validate(string missionId, string chatRef)
    {
        if (string.IsNullOrWhiteSpace(missionId))
            throw new ArgumentException("mission_id is required.", nameof(missionId));
        if (string.IsNullOrWhiteSpace(chatRef))
            throw new ArgumentException("chat_ref is required.", nameof(chatRef));
        if (chatRef.Trim().Length > 256)
            throw new ArgumentException("chat_ref may contain at most 256 characters.", nameof(chatRef));
    }
}