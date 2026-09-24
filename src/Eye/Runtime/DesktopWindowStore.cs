using Microsoft.Data.Sqlite;
using StealthEye.Contract;

namespace StealthEye.Runtime;

public sealed class DesktopWindowStore
{
    private readonly object _gate = new();
    private readonly string _connectionString;

    public DesktopWindowStore(JobStore jobStore)
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

    public DesktopWindowSnapshot Apply(WorkerDesktopObservationResult observation)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            var cursor = NextCursor(connection, transaction);
            var existing = ReadSession(connection, transaction, observation.SessionId)
                .ToDictionary(x => x.Hwnd);
            var current = new List<DesktopWindowState>(observation.Windows.Length);
            var seen = new HashSet<long>();

            foreach (var window in observation.Windows)
            {
                seen.Add(window.Hwnd);
                var processStart = window.ProcessStartAt?.ToString("O");
                string windowId;
                long incarnation;
                if (!existing.TryGetValue(window.Hwnd, out var prior))
                {
                    windowId = "window_" + Guid.NewGuid().ToString("N");
                    incarnation = 1;
                }
                else
                {
                    windowId = prior.WindowId;
                    var replaced = !prior.Active ||
                        prior.ProcessId != window.ProcessId ||
                        !string.Equals(prior.ProcessStartUtc, processStart, StringComparison.Ordinal) ||
                        !string.Equals(prior.ClassName, window.ClassName, StringComparison.Ordinal);
                    incarnation = replaced ? prior.Incarnation + 1 : prior.Incarnation;
                }

                Upsert(connection, transaction, observation.SessionId, windowId, incarnation, window, processStart, observation.ObservedAt);
                current.Add(ToState(windowId, incarnation, window));
            }

            foreach (var prior in existing.Values.Where(x => x.Active && !seen.Contains(x.Hwnd)))
                MarkInactive(connection, transaction, observation.SessionId, prior.Hwnd, observation.ObservedAt);

            transaction.Commit();
            return new DesktopWindowSnapshot(cursor, observation.SessionId, observation.ObservedAt, [.. current]);
        }
    }

    public DesktopWindowTarget ResolveActive(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            throw new ArgumentException("window_id is required.", nameof(windowId));
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT incarnation, session_id, hwnd FROM desktop_windows WHERE window_id = $window_id AND active = 1;";
            command.Parameters.AddWithValue("$window_id", windowId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                throw new ArgumentException($"Unknown or inactive window_id: {windowId}", nameof(windowId));
            return new DesktopWindowTarget(windowId, reader.GetInt64(0), reader.GetInt32(1), reader.GetInt64(2));
        }
    }
    private void Initialize()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS desktop_windows (
                    window_id TEXT NOT NULL,
                    session_id INTEGER NOT NULL,
                    hwnd INTEGER NOT NULL,
                    incarnation INTEGER NOT NULL,
                    active INTEGER NOT NULL,
                    process_id INTEGER NOT NULL,
                    process_start_utc TEXT NULL,
                    process_name TEXT NOT NULL,
                    thread_id INTEGER NOT NULL,
                    title TEXT NOT NULL,
                    class_name TEXT NOT NULL,
                    visible INTEGER NOT NULL,
                    minimized INTEGER NOT NULL,
                    foreground INTEGER NOT NULL,
                    left_px INTEGER NOT NULL,
                    top_px INTEGER NOT NULL,
                    right_px INTEGER NOT NULL,
                    bottom_px INTEGER NOT NULL,
                    first_seen_utc TEXT NOT NULL,
                    last_seen_utc TEXT NOT NULL,
                    PRIMARY KEY (session_id, hwnd)
                );
                CREATE TABLE IF NOT EXISTS desktop_state (
                    key TEXT PRIMARY KEY,
                    value INTEGER NOT NULL
                );
                INSERT OR IGNORE INTO desktop_state (key, value) VALUES ('observation_cursor', 0);
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
            update.CommandText = "UPDATE desktop_state SET value = value + 1 WHERE key = 'observation_cursor';";
            update.ExecuteNonQuery();
        }
        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT value FROM desktop_state WHERE key = 'observation_cursor';";
        return (long)(read.ExecuteScalar() ?? throw new InvalidOperationException("Desktop cursor state is missing."));
    }

    private static DesktopRow[] ReadSession(SqliteConnection connection, SqliteTransaction transaction, int sessionId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT window_id, hwnd, incarnation, active, process_id, process_start_utc, class_name FROM desktop_windows WHERE session_id = $session_id;";
        command.Parameters.AddWithValue("$session_id", sessionId);
        using var reader = command.ExecuteReader();
        var rows = new List<DesktopRow>();
        while (reader.Read())
        {
            rows.Add(new DesktopRow(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3) != 0,
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6)));
        }
        return [.. rows];
    }

    private static void Upsert(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int sessionId,
        string windowId,
        long incarnation,
        WorkerWindowInfo window,
        string? processStart,
        DateTimeOffset observedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO desktop_windows (
                window_id, session_id, hwnd, incarnation, active, process_id, process_start_utc,
                process_name, thread_id, title, class_name, visible, minimized, foreground,
                left_px, top_px, right_px, bottom_px, first_seen_utc, last_seen_utc)
            VALUES (
                $window_id, $session_id, $hwnd, $incarnation, 1, $process_id, $process_start_utc,
                $process_name, $thread_id, $title, $class_name, $visible, $minimized, $foreground,
                $left, $top, $right, $bottom, $observed_at, $observed_at)
            ON CONFLICT(session_id, hwnd) DO UPDATE SET
                window_id = excluded.window_id,
                incarnation = excluded.incarnation,
                active = 1,
                process_id = excluded.process_id,
                process_start_utc = excluded.process_start_utc,
                process_name = excluded.process_name,
                thread_id = excluded.thread_id,
                title = excluded.title,
                class_name = excluded.class_name,
                visible = excluded.visible,
                minimized = excluded.minimized,
                foreground = excluded.foreground,
                left_px = excluded.left_px,
                top_px = excluded.top_px,
                right_px = excluded.right_px,
                bottom_px = excluded.bottom_px,
                last_seen_utc = excluded.last_seen_utc;
            """;
        command.Parameters.AddWithValue("$window_id", windowId);
        command.Parameters.AddWithValue("$session_id", sessionId);
        command.Parameters.AddWithValue("$hwnd", window.Hwnd);
        command.Parameters.AddWithValue("$incarnation", incarnation);
        command.Parameters.AddWithValue("$process_id", window.ProcessId);
        command.Parameters.AddWithValue("$process_start_utc", (object?)processStart ?? DBNull.Value);
        command.Parameters.AddWithValue("$process_name", window.ProcessName);
        command.Parameters.AddWithValue("$thread_id", window.ThreadId);
        command.Parameters.AddWithValue("$title", window.Title);
        command.Parameters.AddWithValue("$class_name", window.ClassName);
        command.Parameters.AddWithValue("$visible", window.Visible ? 1 : 0);
        command.Parameters.AddWithValue("$minimized", window.Minimized ? 1 : 0);
        command.Parameters.AddWithValue("$foreground", window.Foreground ? 1 : 0);
        command.Parameters.AddWithValue("$left", window.Bounds.Left);
        command.Parameters.AddWithValue("$top", window.Bounds.Top);
        command.Parameters.AddWithValue("$right", window.Bounds.Right);
        command.Parameters.AddWithValue("$bottom", window.Bounds.Bottom);
        command.Parameters.AddWithValue("$observed_at", observedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void MarkInactive(SqliteConnection connection, SqliteTransaction transaction, int sessionId, long hwnd, DateTimeOffset observedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE desktop_windows SET active = 0, last_seen_utc = $observed_at WHERE session_id = $session_id AND hwnd = $hwnd;";
        command.Parameters.AddWithValue("$observed_at", observedAt.ToString("O"));
        command.Parameters.AddWithValue("$session_id", sessionId);
        command.Parameters.AddWithValue("$hwnd", hwnd);
        command.ExecuteNonQuery();
    }

    private static DesktopWindowState ToState(string windowId, long incarnation, WorkerWindowInfo window) => new(
        windowId,
        incarnation,
        window.Hwnd,
        window.ProcessId,
        window.ProcessStartAt,
        window.ProcessName,
        window.ThreadId,
        window.Title,
        window.ClassName,
        window.Visible,
        window.Minimized,
        window.Foreground,
        window.Bounds.Left,
        window.Bounds.Top,
        window.Bounds.Right,
        window.Bounds.Bottom,
        window.Uia is null ? null : new DesktopUiaRootState(
            window.Uia.Name,
            window.Uia.AutomationId,
            window.Uia.ControlType,
            window.Uia.FrameworkId,
            window.Uia.ClassName,
            window.Uia.Enabled,
            window.Uia.Offscreen));

    private sealed record DesktopRow(
        string WindowId,
        long Hwnd,
        long Incarnation,
        bool Active,
        int ProcessId,
        string? ProcessStartUtc,
        string ClassName);
}