using Microsoft.Data.Sqlite;

namespace StealthEye.Runtime;

public sealed class TriggerStore
{
    private readonly object _gate = new();
    private readonly string _connectionString;

    public TriggerStore(JobStore jobStore)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = jobStore.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
        Initialize();
    }

    public TriggerRecord CreateProcessExit(int processId, DateTimeOffset? processStartAt, DateTimeOffset? deadlineAt)
    {
        if (processId <= 0)
            throw new ArgumentException("process_id must be positive.", nameof(processId));
        return Insert(new TriggerRecord(
            NewId(), 1, TriggerKinds.ProcessExit, TriggerStates.Pending,
            DateTimeOffset.UtcNow, null, deadlineAt, processId, processStartAt, null, null, null));
    }

    public TriggerRecord CreateTime(DateTimeOffset dueAt)
    {
        return Insert(new TriggerRecord(
            NewId(), 1, TriggerKinds.Time, TriggerStates.Pending,
            DateTimeOffset.UtcNow, null, null, null, null, dueAt, null, null));
    }

    public TriggerRecord CreateFileExists(string filePath, DateTimeOffset? deadlineAt)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("file_path is required.", nameof(filePath));
        return Insert(new TriggerRecord(
            NewId(), 1, TriggerKinds.FileExists, TriggerStates.Pending,
            DateTimeOffset.UtcNow, null, deadlineAt, null, null, null, Path.GetFullPath(filePath), null));
    }
    public TriggerRecord GetRequired(string triggerId)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM triggers WHERE trigger_id = $trigger_id;";
            command.Parameters.AddWithValue("$trigger_id", triggerId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                throw new ArgumentException($"Unknown trigger_id: {triggerId}", nameof(triggerId));
            return ReadTrigger(reader);
        }
    }

    public TriggerRecord[] GetPending()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM triggers WHERE state = $state ORDER BY created_utc;";
            command.Parameters.AddWithValue("$state", TriggerStates.Pending);
            using var reader = command.ExecuteReader();
            var items = new List<TriggerRecord>();
            while (reader.Read()) items.Add(ReadTrigger(reader));
            return [.. items];
        }
    }

    public TriggerRecord Complete(string triggerId, string state, string eventType, string payloadJson, string? failureMessage = null)
    {
        if (!TriggerStates.IsTerminal(state))
            throw new ArgumentException($"State is not terminal: {state}", nameof(state));

        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            long sequence;
            using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = "SELECT state, next_sequence FROM triggers WHERE trigger_id = $trigger_id;";
                select.Parameters.AddWithValue("$trigger_id", triggerId);
                using var reader = select.ExecuteReader();
                if (!reader.Read())
                    throw new ArgumentException($"Unknown trigger_id: {triggerId}", nameof(triggerId));
                if (!string.Equals(reader.GetString(0), TriggerStates.Pending, StringComparison.Ordinal))
                {
                    transaction.Rollback();
                    return GetRequired(triggerId);
                }
                sequence = reader.GetInt64(1);
            }

            var completedAt = DateTimeOffset.UtcNow;
            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE triggers
                    SET state = $state,
                        completed_utc = $completed_utc,
                        failure_message = $failure_message,
                        next_sequence = $next_sequence
                    WHERE trigger_id = $trigger_id AND state = $pending;
                    """;
                update.Parameters.AddWithValue("$state", state);
                update.Parameters.AddWithValue("$completed_utc", completedAt.ToString("O"));
                update.Parameters.AddWithValue("$failure_message", (object?)failureMessage ?? DBNull.Value);
                update.Parameters.AddWithValue("$next_sequence", sequence + 1);
                update.Parameters.AddWithValue("$trigger_id", triggerId);
                update.Parameters.AddWithValue("$pending", TriggerStates.Pending);
                if (update.ExecuteNonQuery() != 1)
                {
                    transaction.Rollback();
                    return GetRequired(triggerId);
                }
            }

            using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO trigger_events (trigger_id, sequence, occurred_utc, event_type, payload_json)
                    VALUES ($trigger_id, $sequence, $occurred_utc, $event_type, $payload_json);
                    """;
                insert.Parameters.AddWithValue("$trigger_id", triggerId);
                insert.Parameters.AddWithValue("$sequence", sequence);
                insert.Parameters.AddWithValue("$occurred_utc", completedAt.ToString("O"));
                insert.Parameters.AddWithValue("$event_type", eventType);
                insert.Parameters.AddWithValue("$payload_json", payloadJson);
                insert.ExecuteNonQuery();
            }

            transaction.Commit();
            return GetRequired(triggerId);
        }
    }

    public long GetLatestSequence(string triggerId)
    {
        _ = GetRequired(triggerId);
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE(MAX(sequence), 0) FROM trigger_events WHERE trigger_id = $trigger_id;";
            command.Parameters.AddWithValue("$trigger_id", triggerId);
            return Convert.ToInt64(command.ExecuteScalar());
        }
    }
    public TriggerEvent[] ReadEvents(string triggerId, long cursor, int maxEvents)
    {
        if (cursor < 0)
            throw new ArgumentException("cursor must be non-negative.", nameof(cursor));
        if (maxEvents is < 1 or > 1000)
            throw new ArgumentException("max_events must be between 1 and 1000.", nameof(maxEvents));
        _ = GetRequired(triggerId);

        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT trigger_id, sequence, occurred_utc, event_type, payload_json
                FROM trigger_events
                WHERE trigger_id = $trigger_id AND sequence > $cursor
                ORDER BY sequence
                LIMIT $max_events;
                """;
            command.Parameters.AddWithValue("$trigger_id", triggerId);
            command.Parameters.AddWithValue("$cursor", cursor);
            command.Parameters.AddWithValue("$max_events", maxEvents);
            using var reader = command.ExecuteReader();
            var events = new List<TriggerEvent>();
            while (reader.Read())
            {
                events.Add(new TriggerEvent(
                    reader.GetString(0),
                    reader.GetInt64(1),
                    DateTimeOffset.Parse(reader.GetString(2)),
                    reader.GetString(3),
                    reader.GetString(4)));
            }
            return [.. events];
        }
    }

    private TriggerRecord Insert(TriggerRecord record)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO triggers (
                    trigger_id, incarnation, kind, state, created_utc, completed_utc, deadline_utc,
                    process_id, process_start_utc, due_utc, file_path, failure_message, next_sequence)
                VALUES (
                    $trigger_id, $incarnation, $kind, $state, $created_utc, NULL, $deadline_utc,
                    $process_id, $process_start_utc, $due_utc, $file_path, NULL, 1);
                """;
            command.Parameters.AddWithValue("$trigger_id", record.TriggerId);
            command.Parameters.AddWithValue("$incarnation", record.Incarnation);
            command.Parameters.AddWithValue("$kind", record.Kind);
            command.Parameters.AddWithValue("$state", record.State);
            command.Parameters.AddWithValue("$created_utc", record.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("$deadline_utc", (object?)record.DeadlineAt?.ToString("O") ?? DBNull.Value);
            command.Parameters.AddWithValue("$process_id", (object?)record.ProcessId ?? DBNull.Value);
            command.Parameters.AddWithValue("$process_start_utc", (object?)record.ProcessStartAt?.ToString("O") ?? DBNull.Value);
            command.Parameters.AddWithValue("$due_utc", (object?)record.DueAt?.ToString("O") ?? DBNull.Value);
            command.Parameters.AddWithValue("$file_path", (object?)record.FilePath ?? DBNull.Value);
            command.ExecuteNonQuery();
            return record;
        }
    }

    private void Initialize()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS triggers (
                    trigger_id TEXT PRIMARY KEY,
                    incarnation INTEGER NOT NULL,
                    kind TEXT NOT NULL,
                    state TEXT NOT NULL,
                    created_utc TEXT NOT NULL,
                    completed_utc TEXT NULL,
                    deadline_utc TEXT NULL,
                    process_id INTEGER NULL,
                    process_start_utc TEXT NULL,
                    due_utc TEXT NULL,
                    file_path TEXT NULL,
                    failure_message TEXT NULL,
                    next_sequence INTEGER NOT NULL DEFAULT 1
                );
                CREATE INDEX IF NOT EXISTS ix_triggers_state ON triggers(state);
                CREATE TABLE IF NOT EXISTS trigger_events (
                    trigger_id TEXT NOT NULL,
                    sequence INTEGER NOT NULL,
                    occurred_utc TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    payload_json TEXT NOT NULL,
                    PRIMARY KEY (trigger_id, sequence),
                    FOREIGN KEY (trigger_id) REFERENCES triggers(trigger_id) ON DELETE CASCADE
                );
                """;
            command.ExecuteNonQuery();
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static TriggerRecord ReadTrigger(SqliteDataReader reader) => new(
        reader.GetString(reader.GetOrdinal("trigger_id")),
        reader.GetInt64(reader.GetOrdinal("incarnation")),
        reader.GetString(reader.GetOrdinal("kind")),
        reader.GetString(reader.GetOrdinal("state")),
        DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_utc"))),
        GetNullableDateTime(reader, "completed_utc"),
        GetNullableDateTime(reader, "deadline_utc"),
        GetNullableInt32(reader, "process_id"),
        GetNullableDateTime(reader, "process_start_utc"),
        GetNullableDateTime(reader, "due_utc"),
        GetNullableString(reader, "file_path"),
        GetNullableString(reader, "failure_message"));

    private static string? GetNullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? GetNullableDateTime(SqliteDataReader reader, string name)
    {
        var value = GetNullableString(reader, name);
        return value is null ? null : DateTimeOffset.Parse(value);
    }

    private static int? GetNullableInt32(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static string NewId() => "trigger_" + Guid.NewGuid().ToString("N");
}
