using Microsoft.Data.Sqlite;
using StealthEye.Contract;

namespace StealthEye.Runtime;

public sealed class UiaElementStore
{
    private readonly object _gate = new();
    private readonly string _connectionString;

    public UiaElementStore(JobStore jobStore)
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

    public UiaQuerySnapshot Apply(DesktopWindowTarget window, WorkerUiaQueryResult observation)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            var cursor = NextCursor(connection, transaction);
            var prior = ReadWindow(connection, transaction, window.WindowId)
                .ToDictionary(x => x.RuntimeId, StringComparer.Ordinal);
            var identities = new Dictionary<string, ElementIdentity>(StringComparer.Ordinal);
            var currentRuntimeIds = new HashSet<string>(StringComparer.Ordinal);
            var output = new List<UiaElementState>(observation.Elements.Length);

            foreach (var element in observation.Elements)
            {
                currentRuntimeIds.Add(element.RuntimeId);
                string elementId;
                long incarnation;
                if (!prior.TryGetValue(element.RuntimeId, out var old))
                {
                    elementId = "element_" + Guid.NewGuid().ToString("N");
                    incarnation = 1;
                }
                else
                {
                    elementId = old.ElementId;
                    var replaced = !old.Active ||
                        old.WindowIncarnation != window.Incarnation ||
                        !string.Equals(old.AutomationId, element.AutomationId, StringComparison.Ordinal) ||
                        !string.Equals(old.ControlType, element.ControlType, StringComparison.Ordinal) ||
                        !string.Equals(old.FrameworkId, element.FrameworkId, StringComparison.Ordinal) ||
                        !string.Equals(old.ClassName, element.ClassName, StringComparison.Ordinal);
                    incarnation = replaced ? old.Incarnation + 1 : old.Incarnation;
                }

                identities[element.RuntimeId] = new ElementIdentity(elementId, incarnation);
                var parentElementId = element.ParentRuntimeId is null
                    ? null
                    : identities.TryGetValue(element.ParentRuntimeId, out var parent)
                        ? parent.ElementId
                        : prior.TryGetValue(element.ParentRuntimeId, out var priorParent)
                            ? priorParent.ElementId
                            : null;

                Upsert(connection, transaction, window, elementId, incarnation, element, observation.ObservedAt);
                output.Add(new UiaElementState(
                    elementId,
                    incarnation,
                    parentElementId,
                    element.Depth,
                    element.Name,
                    element.AutomationId,
                    element.ControlType,
                    element.FrameworkId,
                    element.ClassName,
                    element.Enabled,
                    element.Offscreen,
                    element.Focused,
                    element.Bounds.Left,
                    element.Bounds.Top,
                    element.Bounds.Right,
                    element.Bounds.Bottom));
            }

            if (!observation.Truncated)
            {
                foreach (var old in prior.Values.Where(x => x.Active && !currentRuntimeIds.Contains(x.RuntimeId)))
                    MarkInactive(connection, transaction, window.WindowId, old.RuntimeId, observation.ObservedAt);
            }

            transaction.Commit();
            return new UiaQuerySnapshot(
                cursor,
                window.WindowId,
                window.Incarnation,
                observation.ObservedAt,
                observation.Truncated,
                [.. output]);
        }
    }

    public UiaElementTarget ResolveActive(string elementId)
    {
        if (string.IsNullOrWhiteSpace(elementId))
            throw new ArgumentException("element_id is required.", nameof(elementId));
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT incarnation, window_id, window_incarnation, runtime_id FROM uia_elements WHERE element_id = $element_id AND active = 1;";
            command.Parameters.AddWithValue("$element_id", elementId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                throw new ArgumentException($"Unknown or inactive element_id: {elementId}", nameof(elementId));
            return new UiaElementTarget(
                elementId,
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetString(3));
        }
    }
    public UiaElementTarget? TryResolveActiveByRuntimeId(string windowId, string runtimeId)
    {
        if (string.IsNullOrWhiteSpace(windowId) || string.IsNullOrWhiteSpace(runtimeId))
            return null;
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT element_id, incarnation, window_incarnation FROM uia_elements WHERE window_id = $window_id AND runtime_id = $runtime_id AND active = 1;";
            command.Parameters.AddWithValue("$window_id", windowId);
            command.Parameters.AddWithValue("$runtime_id", runtimeId);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            return new UiaElementTarget(reader.GetString(0), reader.GetInt64(1), windowId, reader.GetInt64(2), runtimeId);
        }
    }
    private void Initialize()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS uia_elements (
                    element_id TEXT NOT NULL,
                    window_id TEXT NOT NULL,
                    window_incarnation INTEGER NOT NULL,
                    runtime_id TEXT NOT NULL,
                    incarnation INTEGER NOT NULL,
                    active INTEGER NOT NULL,
                    automation_id TEXT NOT NULL,
                    control_type TEXT NOT NULL,
                    framework_id TEXT NOT NULL,
                    class_name TEXT NOT NULL,
                    last_seen_utc TEXT NOT NULL,
                    PRIMARY KEY (window_id, runtime_id)
                );
                CREATE TABLE IF NOT EXISTS uia_state (
                    key TEXT PRIMARY KEY,
                    value INTEGER NOT NULL
                );
                INSERT OR IGNORE INTO uia_state (key, value) VALUES ('observation_cursor', 0);
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
            update.CommandText = "UPDATE uia_state SET value = value + 1 WHERE key = 'observation_cursor';";
            update.ExecuteNonQuery();
        }
        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT value FROM uia_state WHERE key = 'observation_cursor';";
        return (long)(read.ExecuteScalar() ?? throw new InvalidOperationException("UIA cursor state is missing."));
    }

    private static UiaRow[] ReadWindow(SqliteConnection connection, SqliteTransaction transaction, string windowId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT element_id, runtime_id, incarnation, active, window_incarnation, automation_id, control_type, framework_id, class_name FROM uia_elements WHERE window_id = $window_id;";
        command.Parameters.AddWithValue("$window_id", windowId);
        using var reader = command.ExecuteReader();
        var rows = new List<UiaRow>();
        while (reader.Read())
        {
            rows.Add(new UiaRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetInt64(3) != 0,
                reader.GetInt64(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8)));
        }
        return [.. rows];
    }

    private static void Upsert(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DesktopWindowTarget window,
        string elementId,
        long incarnation,
        WorkerUiaElementInfo element,
        DateTimeOffset observedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO uia_elements (
                element_id, window_id, window_incarnation, runtime_id, incarnation, active,
                automation_id, control_type, framework_id, class_name, last_seen_utc)
            VALUES (
                $element_id, $window_id, $window_incarnation, $runtime_id, $incarnation, 1,
                $automation_id, $control_type, $framework_id, $class_name, $observed_at)
            ON CONFLICT(window_id, runtime_id) DO UPDATE SET
                element_id = excluded.element_id,
                window_incarnation = excluded.window_incarnation,
                incarnation = excluded.incarnation,
                active = 1,
                automation_id = excluded.automation_id,
                control_type = excluded.control_type,
                framework_id = excluded.framework_id,
                class_name = excluded.class_name,
                last_seen_utc = excluded.last_seen_utc;
            """;
        command.Parameters.AddWithValue("$element_id", elementId);
        command.Parameters.AddWithValue("$window_id", window.WindowId);
        command.Parameters.AddWithValue("$window_incarnation", window.Incarnation);
        command.Parameters.AddWithValue("$runtime_id", element.RuntimeId);
        command.Parameters.AddWithValue("$incarnation", incarnation);
        command.Parameters.AddWithValue("$automation_id", element.AutomationId);
        command.Parameters.AddWithValue("$control_type", element.ControlType);
        command.Parameters.AddWithValue("$framework_id", element.FrameworkId);
        command.Parameters.AddWithValue("$class_name", element.ClassName);
        command.Parameters.AddWithValue("$observed_at", observedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void MarkInactive(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string windowId,
        string runtimeId,
        DateTimeOffset observedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE uia_elements SET active = 0, last_seen_utc = $observed_at WHERE window_id = $window_id AND runtime_id = $runtime_id;";
        command.Parameters.AddWithValue("$observed_at", observedAt.ToString("O"));
        command.Parameters.AddWithValue("$window_id", windowId);
        command.Parameters.AddWithValue("$runtime_id", runtimeId);
        command.ExecuteNonQuery();
    }

    private sealed record ElementIdentity(string ElementId, long Incarnation);
    private sealed record UiaRow(
        string ElementId,
        string RuntimeId,
        long Incarnation,
        bool Active,
        long WindowIncarnation,
        string AutomationId,
        string ControlType,
        string FrameworkId,
        string ClassName);
}