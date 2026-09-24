using Microsoft.Data.Sqlite;
using StealthEye.Contract;

namespace StealthEye.Runtime;

public sealed class BrowserTargetStore
{
    private readonly object _gate = new();
    private readonly string _connectionString;

    public BrowserTargetStore(JobStore jobStore)
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

    public BrowserTargetSnapshot Apply(WorkerBrowserTargetsResult observation)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            var cursor = NextCursor(connection, transaction);
            var existing = ReadAll(connection, transaction).ToDictionary(x => x.CdpTargetId, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var current = new List<BrowserTargetState>(observation.Targets.Length);

            foreach (var target in observation.Targets)
            {
                if (string.IsNullOrWhiteSpace(target.CdpTargetId))
                    continue;
                seen.Add(target.CdpTargetId);
                string targetId;
                long incarnation;
                if (!existing.TryGetValue(target.CdpTargetId, out var prior))
                {
                    targetId = "target_" + Guid.NewGuid().ToString("N");
                    incarnation = 1;
                }
                else
                {
                    targetId = prior.TargetId;
                    var replaced = !prior.Active || !string.Equals(prior.Type, target.Type, StringComparison.Ordinal);
                    incarnation = replaced ? prior.Incarnation + 1 : prior.Incarnation;
                }

                Upsert(connection, transaction, targetId, incarnation, target, observation.ObservedAt);
                current.Add(new BrowserTargetState(targetId, incarnation, target.Type, target.Title, target.Url));
            }

            foreach (var prior in existing.Values.Where(x => x.Active && !seen.Contains(x.CdpTargetId)))
                MarkInactive(connection, transaction, prior.CdpTargetId, observation.ObservedAt);

            transaction.Commit();
            return new BrowserTargetSnapshot(
                cursor,
                observation.ObservedAt,
                observation.BrowserVersion,
                observation.ProtocolVersion,
                [.. current]);
        }
    }

    public BrowserTargetHandle ResolveActive(string targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
            throw new ArgumentException("target_id is required.", nameof(targetId));
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT incarnation, cdp_target_id, type, url FROM browser_targets WHERE target_id = $target_id AND active = 1;";
            command.Parameters.AddWithValue("$target_id", targetId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                throw new ArgumentException($"Unknown or inactive target_id: {targetId}", nameof(targetId));
            return new BrowserTargetHandle(
                targetId,
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3));
        }
    }

    private void Initialize()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS browser_targets (
                    target_id TEXT NOT NULL UNIQUE,
                    cdp_target_id TEXT PRIMARY KEY,
                    incarnation INTEGER NOT NULL,
                    active INTEGER NOT NULL,
                    type TEXT NOT NULL,
                    title TEXT NOT NULL,
                    url TEXT NOT NULL,
                    first_seen_utc TEXT NOT NULL,
                    last_seen_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS browser_state (
                    key TEXT PRIMARY KEY,
                    value INTEGER NOT NULL
                );
                INSERT OR IGNORE INTO browser_state (key, value) VALUES ('observation_cursor', 0);
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

    private static long NextCursor(SqliteConnection connection, SqliteTransaction transaction)
    {
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE browser_state SET value = value + 1 WHERE key = 'observation_cursor';";
            update.ExecuteNonQuery();
        }
        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT value FROM browser_state WHERE key = 'observation_cursor';";
        return (long)(read.ExecuteScalar() ?? throw new InvalidOperationException("Browser cursor state is missing."));
    }

    private static BrowserRow[] ReadAll(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT target_id, cdp_target_id, incarnation, active, type FROM browser_targets;";
        using var reader = command.ExecuteReader();
        var rows = new List<BrowserRow>();
        while (reader.Read())
        {
            rows.Add(new BrowserRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetInt64(3) != 0,
                reader.GetString(4)));
        }
        return [.. rows];
    }

    private static void Upsert(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string targetId,
        long incarnation,
        WorkerBrowserTargetInfo target,
        DateTimeOffset observedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO browser_targets (
                target_id, cdp_target_id, incarnation, active, type, title, url, first_seen_utc, last_seen_utc)
            VALUES (
                $target_id, $cdp_target_id, $incarnation, 1, $type, $title, $url, $observed_at, $observed_at)
            ON CONFLICT(cdp_target_id) DO UPDATE SET
                target_id = excluded.target_id,
                incarnation = excluded.incarnation,
                active = 1,
                type = excluded.type,
                title = excluded.title,
                url = excluded.url,
                last_seen_utc = excluded.last_seen_utc;
            """;
        command.Parameters.AddWithValue("$target_id", targetId);
        command.Parameters.AddWithValue("$cdp_target_id", target.CdpTargetId);
        command.Parameters.AddWithValue("$incarnation", incarnation);
        command.Parameters.AddWithValue("$type", target.Type);
        command.Parameters.AddWithValue("$title", target.Title);
        command.Parameters.AddWithValue("$url", target.Url);
        command.Parameters.AddWithValue("$observed_at", observedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void MarkInactive(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string cdpTargetId,
        DateTimeOffset observedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE browser_targets SET active = 0, last_seen_utc = $observed_at WHERE cdp_target_id = $cdp_target_id;";
        command.Parameters.AddWithValue("$observed_at", observedAt.ToString("O"));
        command.Parameters.AddWithValue("$cdp_target_id", cdpTargetId);
        command.ExecuteNonQuery();
    }

    private sealed record BrowserRow(
        string TargetId,
        string CdpTargetId,
        long Incarnation,
        bool Active,
        string Type);
}