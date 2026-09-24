using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace StealthEye.Runtime;

public sealed class ActionJournalStore
{
    private const string ZeroHash = "0000000000000000000000000000000000000000000000000000000000000000";
    private readonly object _gate = new();
    private readonly string _connectionString;

    public ActionJournalStore(JobStore jobs)
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

    public ActionReservation Reserve(
        string taskId,
        string actionId,
        string capability,
        string inputSha256,
        ActionPostconditionContract? postcondition = null)
    {
        RequireText(taskId, nameof(taskId));
        RequireText(actionId, nameof(actionId));
        RequireText(capability, nameof(capability));
        ValidateSha256(inputSha256, nameof(inputSha256));
        if (postcondition is not null)
        {
            RequireText(postcondition.Kind, nameof(postcondition.Kind));
            RequireText(postcondition.SpecJson, nameof(postcondition.SpecJson));
        }

        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();

            var existing = TryGet(connection, transaction, actionId);
            if (existing is not null)
            {
                if (!string.Equals(existing.InputSha256, inputSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"action_id '{actionId}' was already reserved with different inputs.");

                var disposition = existing.State switch
                {
                    ActionStates.Verified or ActionStates.Failed => ActionReservationDisposition.ReturnPrior,
                    ActionStates.OutcomeUnknown => ActionReservationDisposition.InspectBeforeReplay,
                    ActionStates.Ready => ReReserve(connection, transaction, existing),
                    ActionStates.Reserved or ActionStates.Dispatching or ActionStates.Running => ActionReservationDisposition.InProgress,
                    _ => throw new InvalidOperationException($"Unknown action state '{existing.State}'.")
                };

                transaction.Commit();
                return new ActionReservation(disposition, GetRequired(actionId));
            }

            var now = DateTimeOffset.UtcNow;
            using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO actions (
                        action_id, task_id, capability, input_sha256, state,
                        postcondition_kind, postcondition_spec_json,
                        created_utc, updated_utc)
                    VALUES (
                        $action_id, $task_id, $capability, $input_sha256, $state,
                        $postcondition_kind, $postcondition_spec_json,
                        $created_utc, $updated_utc);
                    """;
                insert.Parameters.AddWithValue("$action_id", actionId);
                insert.Parameters.AddWithValue("$task_id", taskId);
                insert.Parameters.AddWithValue("$capability", capability);
                insert.Parameters.AddWithValue("$input_sha256", inputSha256.ToLowerInvariant());
                insert.Parameters.AddWithValue("$state", ActionStates.Reserved);
                insert.Parameters.AddWithValue("$postcondition_kind", (object?)postcondition?.Kind ?? DBNull.Value);
                insert.Parameters.AddWithValue("$postcondition_spec_json", (object?)postcondition?.SpecJson ?? DBNull.Value);
                insert.Parameters.AddWithValue("$created_utc", now.ToString("O"));
                insert.Parameters.AddWithValue("$updated_utc", now.ToString("O"));
                insert.ExecuteNonQuery();
            }

            using (var lockInsert = connection.CreateCommand())
            {
                lockInsert.Transaction = transaction;
                lockInsert.CommandText = """
                    INSERT INTO idempotency_locks (action_id, input_sha256, acquired_utc)
                    VALUES ($action_id, $input_sha256, $acquired_utc);
                    """;
                lockInsert.Parameters.AddWithValue("$action_id", actionId);
                lockInsert.Parameters.AddWithValue("$input_sha256", inputSha256.ToLowerInvariant());
                lockInsert.Parameters.AddWithValue("$acquired_utc", now.ToString("O"));
                lockInsert.ExecuteNonQuery();
            }

            AppendLedger(connection, transaction, actionId, "reserved", JsonSerializer.Serialize(new
            {
                task_id = taskId,
                capability,
                input_sha256 = inputSha256.ToLowerInvariant(),
                state = ActionStates.Reserved
            }));

            transaction.Commit();
            return new ActionReservation(ActionReservationDisposition.Execute, GetRequired(actionId));
        }
    }

    public ActionRecord MarkDispatching(string actionId) =>
        Transition(actionId, [ActionStates.Reserved], ActionStates.Dispatching, "dispatching");

    public ActionRecord MarkRunning(string actionId) =>
        Transition(actionId, [ActionStates.Dispatching], ActionStates.Running, "running");

    public ActionRecord MarkVerified(string actionId, string evidenceJson, string? resultJson = null)
    {
        RequireJson(evidenceJson, nameof(evidenceJson));
        if (resultJson is not null)
            RequireJson(resultJson, nameof(resultJson));

        return Transition(
            actionId,
            [ActionStates.Reserved, ActionStates.Dispatching, ActionStates.Running, ActionStates.OutcomeUnknown],
            ActionStates.Verified,
            "verified",
            evidenceJson,
            resultJson,
            setVerifiedAt: true);
    }

    public ActionRecord MarkOutcomeUnknown(string actionId, string evidenceJson, string? resultJson = null)
    {
        RequireJson(evidenceJson, nameof(evidenceJson));
        if (resultJson is not null)
            RequireJson(resultJson, nameof(resultJson));

        return Transition(
            actionId,
            [ActionStates.Dispatching, ActionStates.Running],
            ActionStates.OutcomeUnknown,
            "outcome_unknown",
            evidenceJson,
            resultJson);
    }
    public ActionRecord MarkFailed(string actionId, string evidenceJson, string? resultJson = null)
    {
        RequireJson(evidenceJson, nameof(evidenceJson));
        if (resultJson is not null)
            RequireJson(resultJson, nameof(resultJson));

        return Transition(
            actionId,
            [ActionStates.Reserved, ActionStates.Dispatching, ActionStates.Running, ActionStates.OutcomeUnknown],
            ActionStates.Failed,
            "failed",
            evidenceJson,
            resultJson);
    }

    public ActionRecord ReleaseForRetryAfterInspection(string actionId, string evidenceJson)
    {
        RequireJson(evidenceJson, nameof(evidenceJson));
        return Transition(
            actionId,
            [ActionStates.OutcomeUnknown],
            ActionStates.Ready,
            "inspection_authorized_retry",
            evidenceJson,
            resultJson: null);
    }

    public ActionRecord GetRequired(string actionId)
    {
        lock (_gate)
        {
            using var connection = Open();
            var record = TryGet(connection, null, actionId);
            return record ?? throw new ArgumentException($"Unknown action_id: {actionId}", nameof(actionId));
        }
    }

    public ActionRecord[] ListOutcomeUnknown(int limit = 100)
    {
        if (limit is < 1 or > 1000)
            throw new ArgumentException("limit must be between 1 and 1000.", nameof(limit));

        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT *
                FROM actions
                WHERE state = $state
                ORDER BY updated_utc ASC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$state", ActionStates.OutcomeUnknown);
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader();
            var records = new List<ActionRecord>();
            while (reader.Read())
                records.Add(Read(reader));
            return [.. records];
        }
    }

    public bool HasIdempotencyLock(string actionId)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM idempotency_locks WHERE action_id = $action_id LIMIT 1;";
            command.Parameters.AddWithValue("$action_id", actionId);
            return command.ExecuteScalar() is not null;
        }
    }

    public bool VerifyLedger()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT action_id, event_type, payload_json, previous_hash, record_hash, created_utc
                FROM action_ledger
                ORDER BY seq ASC;
                """;

            using var reader = command.ExecuteReader();
            var expectedPrevious = ZeroHash;
            while (reader.Read())
            {
                var actionId = reader.GetString(0);
                var eventType = reader.GetString(1);
                var payloadJson = reader.GetString(2);
                var previousHash = reader.GetString(3);
                var recordHash = reader.GetString(4);
                var createdUtc = reader.GetString(5);

                if (!string.Equals(previousHash, expectedPrevious, StringComparison.OrdinalIgnoreCase))
                    return false;

                var expectedRecord = ComputeLedgerHash(previousHash, actionId, eventType, payloadJson, createdUtc);
                if (!string.Equals(recordHash, expectedRecord, StringComparison.OrdinalIgnoreCase))
                    return false;

                expectedPrevious = recordHash;
            }

            return true;
        }
    }

    public int RecoverAfterHostRestart()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            var recoverable = new List<(string ActionId, string State)>();
            using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = """
                    SELECT action_id, state
                    FROM actions
                    WHERE state IN ($reserved, $dispatching, $running);
                    """;
                select.Parameters.AddWithValue("$reserved", ActionStates.Reserved);
                select.Parameters.AddWithValue("$dispatching", ActionStates.Dispatching);
                select.Parameters.AddWithValue("$running", ActionStates.Running);
                using var reader = select.ExecuteReader();
                while (reader.Read())
                    recoverable.Add((reader.GetString(0), reader.GetString(1)));
            }

            foreach (var item in recoverable)
            {
                var recoveredState = item.State == ActionStates.Reserved
                    ? ActionStates.Ready
                    : ActionStates.OutcomeUnknown;
                var now = DateTimeOffset.UtcNow.ToString("O");

                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE actions
                    SET state = $state, updated_utc = $updated_utc
                    WHERE action_id = $action_id;
                    """;
                update.Parameters.AddWithValue("$state", recoveredState);
                update.Parameters.AddWithValue("$updated_utc", now);
                update.Parameters.AddWithValue("$action_id", item.ActionId);
                update.ExecuteNonQuery();

                AppendLedger(connection, transaction, item.ActionId, "host_recovery", JsonSerializer.Serialize(new
                {
                    prior_state = item.State,
                    state = recoveredState,
                    replay_allowed = recoveredState == ActionStates.Ready
                }));
            }

            transaction.Commit();
            return recoverable.Count;
        }
    }
    private ActionReservationDisposition ReReserve(SqliteConnection connection, SqliteTransaction transaction, ActionRecord existing)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE actions
            SET state = $state, updated_utc = $updated_utc
            WHERE action_id = $action_id AND state = $expected;
            """;
        command.Parameters.AddWithValue("$state", ActionStates.Reserved);
        command.Parameters.AddWithValue("$updated_utc", now);
        command.Parameters.AddWithValue("$action_id", existing.ActionId);
        command.Parameters.AddWithValue("$expected", ActionStates.Ready);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException($"Action '{existing.ActionId}' changed while being reserved.");

        AppendLedger(connection, transaction, existing.ActionId, "reserved_after_recovery", JsonSerializer.Serialize(new
        {
            state = ActionStates.Reserved
        }));
        return ActionReservationDisposition.Execute;
    }

    private ActionRecord Transition(
        string actionId,
        string[] allowedStates,
        string newState,
        string eventType,
        string? evidenceJson = null,
        string? resultJson = null,
        bool setVerifiedAt = false)
    {
        RequireText(actionId, nameof(actionId));

        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            var current = TryGet(connection, transaction, actionId)
                ?? throw new ArgumentException($"Unknown action_id: {actionId}", nameof(actionId));

            if (!allowedStates.Contains(current.State, StringComparer.Ordinal))
                throw new InvalidOperationException(
                    $"Action '{actionId}' cannot transition from '{current.State}' to '{newState}'.");

            var now = DateTimeOffset.UtcNow.ToString("O");
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE actions
                SET state = $state,
                    evidence_json = COALESCE($evidence_json, evidence_json),
                    result_json = COALESCE($result_json, result_json),
                    updated_utc = $updated_utc,
                    verified_utc = CASE WHEN $set_verified = 1 THEN $verified_utc ELSE verified_utc END
                WHERE action_id = $action_id;
                """;
            command.Parameters.AddWithValue("$state", newState);
            command.Parameters.AddWithValue("$evidence_json", (object?)evidenceJson ?? DBNull.Value);
            command.Parameters.AddWithValue("$result_json", (object?)resultJson ?? DBNull.Value);
            command.Parameters.AddWithValue("$updated_utc", now);
            command.Parameters.AddWithValue("$set_verified", setVerifiedAt ? 1 : 0);
            command.Parameters.AddWithValue("$verified_utc", setVerifiedAt ? now : DBNull.Value);
            command.Parameters.AddWithValue("$action_id", actionId);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException($"Action '{actionId}' disappeared during transition.");

            AppendLedger(connection, transaction, actionId, eventType, JsonSerializer.Serialize(new
            {
                prior_state = current.State,
                state = newState,
                evidence_json = evidenceJson,
                result_json = resultJson
            }));

            transaction.Commit();
            return GetRequired(actionId);
        }
    }

    private void Initialize()
    {
        lock (_gate)
        {
            using var connection = Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    PRAGMA journal_mode=WAL;
                    PRAGMA synchronous=NORMAL;
                    PRAGMA foreign_keys=ON;

                    CREATE TABLE IF NOT EXISTS actions (
                        action_id TEXT PRIMARY KEY,
                        task_id TEXT NOT NULL,
                        capability TEXT NOT NULL,
                        input_sha256 TEXT NOT NULL,
                        state TEXT NOT NULL,
                        postcondition_kind TEXT NULL,
                        postcondition_spec_json TEXT NULL,
                        evidence_json TEXT NULL,
                        result_json TEXT NULL,
                        created_utc TEXT NOT NULL,
                        updated_utc TEXT NOT NULL,
                        verified_utc TEXT NULL
                    );

                    CREATE TABLE IF NOT EXISTS idempotency_locks (
                        action_id TEXT PRIMARY KEY,
                        input_sha256 TEXT NOT NULL,
                        acquired_utc TEXT NOT NULL,
                        FOREIGN KEY(action_id) REFERENCES actions(action_id) ON DELETE CASCADE
                    );

                    CREATE TABLE IF NOT EXISTS action_ledger (
                        seq INTEGER PRIMARY KEY AUTOINCREMENT,
                        action_id TEXT NOT NULL,
                        event_type TEXT NOT NULL,
                        payload_json TEXT NOT NULL,
                        previous_hash TEXT NOT NULL,
                        record_hash TEXT NOT NULL,
                        created_utc TEXT NOT NULL,
                        FOREIGN KEY(action_id) REFERENCES actions(action_id) ON DELETE CASCADE
                    );

                    CREATE INDEX IF NOT EXISTS idx_actions_state_updated
                    ON actions(state, updated_utc);
                    """;
                command.ExecuteNonQuery();
            }


        }
    }

    private void AppendLedger(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string actionId,
        string eventType,
        string payloadJson)
    {
        var previousHash = ZeroHash;
        using (var prior = connection.CreateCommand())
        {
            prior.Transaction = transaction;
            prior.CommandText = "SELECT record_hash FROM action_ledger ORDER BY seq DESC LIMIT 1;";
            previousHash = prior.ExecuteScalar() as string ?? ZeroHash;
        }

        var createdUtc = DateTimeOffset.UtcNow.ToString("O");
        var recordHash = ComputeLedgerHash(previousHash, actionId, eventType, payloadJson, createdUtc);

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO action_ledger (
                action_id, event_type, payload_json, previous_hash, record_hash, created_utc)
            VALUES (
                $action_id, $event_type, $payload_json, $previous_hash, $record_hash, $created_utc);
            """;
        insert.Parameters.AddWithValue("$action_id", actionId);
        insert.Parameters.AddWithValue("$event_type", eventType);
        insert.Parameters.AddWithValue("$payload_json", payloadJson);
        insert.Parameters.AddWithValue("$previous_hash", previousHash);
        insert.Parameters.AddWithValue("$record_hash", recordHash);
        insert.Parameters.AddWithValue("$created_utc", createdUtc);
        insert.ExecuteNonQuery();
    }

    private static string ComputeLedgerHash(
        string previousHash,
        string actionId,
        string eventType,
        string payloadJson,
        string createdUtc)
    {
        var bytes = Encoding.UTF8.GetBytes(
            string.Join("|", previousHash, actionId, eventType, payloadJson, createdUtc));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static ActionRecord? TryGet(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string actionId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM actions WHERE action_id = $action_id;";
        command.Parameters.AddWithValue("$action_id", actionId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    private static ActionRecord Read(SqliteDataReader reader)
    {
        var postconditionKind = GetNullableString(reader, "postcondition_kind");
        var postconditionSpec = GetNullableString(reader, "postcondition_spec_json");
        return new ActionRecord(
            reader.GetString(reader.GetOrdinal("action_id")),
            reader.GetString(reader.GetOrdinal("task_id")),
            reader.GetString(reader.GetOrdinal("capability")),
            reader.GetString(reader.GetOrdinal("input_sha256")),
            reader.GetString(reader.GetOrdinal("state")),
            postconditionKind is null || postconditionSpec is null
                ? null
                : new ActionPostconditionContract(postconditionKind, postconditionSpec),
            GetNullableString(reader, "evidence_json"),
            GetNullableString(reader, "result_json"),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_utc"))),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_utc"))),
            GetNullableDateTimeOffset(reader, "verified_utc"));
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static string? GetNullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(SqliteDataReader reader, string name)
    {
        var value = GetNullableString(reader, name);
        return value is null ? null : DateTimeOffset.Parse(value);
    }

    private static void RequireText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name} is required.", name);
    }

    private static void ValidateSha256(string value, string name)
    {
        if (value.Length != 64 || value.Any(ch => !Uri.IsHexDigit(ch)))
            throw new ArgumentException($"{name} must be a 64-character SHA-256 hex digest.", name);
    }

    private static void RequireJson(string value, string name)
    {
        RequireText(value, name);
        try
        {
            using var _ = JsonDocument.Parse(value);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"{name} must contain valid JSON.", name, ex);
        }
    }
}


