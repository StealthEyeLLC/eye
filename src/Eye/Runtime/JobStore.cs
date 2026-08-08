using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace StealthEye.Runtime;

public sealed class JobStore
{
    private static readonly object ProviderGate = new();
    private static bool ProviderInitialized;
    private readonly object _gate = new();
    private readonly string _connectionString;

    public JobStore() : this(null, null)
    {
    }

    public JobStore(string? stateRoot, string? spoolRoot)
    {
        EnsureProvider();
        StateRoot = stateRoot ?? Environment.GetEnvironmentVariable("EYE_STATE_ROOT")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "StealthEye");
        SpoolRoot = spoolRoot ?? Environment.GetEnvironmentVariable("EYE_JOB_ROOT")
            ?? (Directory.Exists(@"X:\") ? @"X:\StealthEye\jobs" : Path.Combine(StateRoot, "jobs"));

        Directory.CreateDirectory(StateRoot);
        Directory.CreateDirectory(SpoolRoot);
        DatabasePath = Path.Combine(StateRoot, "state.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();

        Initialize();
    }

    public string StateRoot { get; }
    public string SpoolRoot { get; }
    public string DatabasePath { get; }

    public JobPaths AllocatePaths(string jobId)
    {
        var directory = Path.Combine(SpoolRoot, jobId);
        Directory.CreateDirectory(directory);
        var stdout = Path.Combine(directory, "stdout.log");
        var stderr = Path.Combine(directory, "stderr.log");
        using (File.Open(stdout, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite)) { }
        using (File.Open(stderr, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite)) { }
        return new JobPaths(directory, stdout, stderr);
    }

    public JobRecord Create(
        string jobId,
        RunRequest request,
        JobPaths paths,
        bool terminal = false,
        int? columns = null,
        int? rows = null)
    {
        if (!terminal)
        {
            columns = null;
            rows = null;
        }

        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO jobs (
                    job_id, incarnation, state, context, file_name, arguments_json, working_directory,
                    timeout_ms, terminal, columns, rows, created_utc, timed_out, stdout_path, stderr_path)
                VALUES ($job_id, 1, $state, $context, $file_name, $arguments, $working_directory,
                    $timeout_ms, $terminal, $columns, $rows, $created_utc, 0, $stdout_path, $stderr_path);
                """;
            command.Parameters.AddWithValue("$job_id", jobId);
            command.Parameters.AddWithValue("$state", JobStates.Starting);
            command.Parameters.AddWithValue("$context", request.Context);
            command.Parameters.AddWithValue("$file_name", request.FileName);
            command.Parameters.AddWithValue("$arguments", JsonSerializer.Serialize(request.Arguments));
            command.Parameters.AddWithValue("$working_directory", (object?)request.WorkingDirectory ?? DBNull.Value);
            command.Parameters.AddWithValue("$timeout_ms", request.TimeoutMs);
            command.Parameters.AddWithValue("$terminal", terminal ? 1 : 0);
            command.Parameters.AddWithValue("$columns", (object?)columns ?? DBNull.Value);
            command.Parameters.AddWithValue("$rows", (object?)rows ?? DBNull.Value);
            command.Parameters.AddWithValue("$created_utc", now.ToString("O"));
            command.Parameters.AddWithValue("$stdout_path", paths.Stdout);
            command.Parameters.AddWithValue("$stderr_path", paths.Stderr);
            command.ExecuteNonQuery();
        }

        return GetRequired(jobId);
    }

    public void MarkRunning(string jobId, int pid, string effectiveIdentity)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE jobs
                SET state = $state, pid = $pid, effective_identity = $identity, started_utc = COALESCE(started_utc, $started_utc)
                WHERE job_id = $job_id;
                """;
            command.Parameters.AddWithValue("$state", JobStates.Running);
            command.Parameters.AddWithValue("$pid", pid);
            command.Parameters.AddWithValue("$identity", effectiveIdentity);
            command.Parameters.AddWithValue("$started_utc", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$job_id", jobId);
            RequireUpdated(command.ExecuteNonQuery(), jobId);
        }
    }

    public JobRecord UpdateTerminalSize(string jobId, int columns, int rows)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE jobs
                SET columns = $columns, rows = $rows
                WHERE job_id = $job_id AND terminal = 1;
                """;
            command.Parameters.AddWithValue("$columns", columns);
            command.Parameters.AddWithValue("$rows", rows);
            command.Parameters.AddWithValue("$job_id", jobId);
            RequireUpdated(command.ExecuteNonQuery(), jobId);
        }

        return GetRequired(jobId);
    }

    public JobRecord Finish(string jobId, string state, ProcessRunResult? result = null, string? failureCode = null, string? failureMessage = null)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE jobs
                SET state = $state,
                    completed_utc = $completed_utc,
                    exit_code = $exit_code,
                    timed_out = $timed_out,
                    pid = COALESCE(pid, $pid),
                    effective_identity = COALESCE(effective_identity, $identity),
                    failure_code = $failure_code,
                    failure_message = $failure_message
                WHERE job_id = $job_id;
                """;
            command.Parameters.AddWithValue("$state", state);
            command.Parameters.AddWithValue("$completed_utc", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$exit_code", result is null ? DBNull.Value : result.ExitCode);
            command.Parameters.AddWithValue("$timed_out", result?.TimedOut == true ? 1 : 0);
            command.Parameters.AddWithValue("$pid", result is null ? DBNull.Value : result.Pid);
            command.Parameters.AddWithValue("$identity", result is null ? DBNull.Value : result.EffectiveIdentity);
            command.Parameters.AddWithValue("$failure_code", (object?)failureCode ?? DBNull.Value);
            command.Parameters.AddWithValue("$failure_message", (object?)failureMessage ?? DBNull.Value);
            command.Parameters.AddWithValue("$job_id", jobId);
            RequireUpdated(command.ExecuteNonQuery(), jobId);
        }

        return GetRequired(jobId);
    }

    public JobRecord GetRequired(string jobId)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM jobs WHERE job_id = $job_id;";
            command.Parameters.AddWithValue("$job_id", jobId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                throw new ArgumentException($"Unknown job_id: {jobId}", nameof(jobId));
            return Read(reader);
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
                    CREATE TABLE IF NOT EXISTS jobs (
                        job_id TEXT PRIMARY KEY,
                        incarnation INTEGER NOT NULL,
                        state TEXT NOT NULL,
                        context TEXT NOT NULL,
                        file_name TEXT NOT NULL,
                        arguments_json TEXT NOT NULL,
                        working_directory TEXT NULL,
                        timeout_ms INTEGER NOT NULL,
                        terminal INTEGER NOT NULL DEFAULT 0,
                        columns INTEGER NULL,
                        rows INTEGER NULL,
                        pid INTEGER NULL,
                        effective_identity TEXT NULL,
                        created_utc TEXT NOT NULL,
                        started_utc TEXT NULL,
                        completed_utc TEXT NULL,
                        exit_code INTEGER NULL,
                        timed_out INTEGER NOT NULL DEFAULT 0,
                        failure_code TEXT NULL,
                        failure_message TEXT NULL,
                        stdout_path TEXT NOT NULL,
                        stderr_path TEXT NOT NULL
                    );
                    """;
                command.ExecuteNonQuery();
            }


            using var recover = connection.CreateCommand();
            recover.CommandText = """
                UPDATE jobs
                SET state = $interrupted,
                    completed_utc = $completed_utc,
                    failure_code = 'host_restarted',
                    failure_message = 'The host restarted; live process handles cannot be recovered.'
                WHERE state IN ($starting, $running);
                """;
            recover.Parameters.AddWithValue("$interrupted", JobStates.Interrupted);
            recover.Parameters.AddWithValue("$completed_utc", DateTimeOffset.UtcNow.ToString("O"));
            recover.Parameters.AddWithValue("$starting", JobStates.Starting);
            recover.Parameters.AddWithValue("$running", JobStates.Running);
            recover.ExecuteNonQuery();
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static JobRecord Read(SqliteDataReader reader)
    {
        return new JobRecord(
            reader.GetString(reader.GetOrdinal("job_id")),
            reader.GetInt64(reader.GetOrdinal("incarnation")),
            reader.GetString(reader.GetOrdinal("state")),
            reader.GetString(reader.GetOrdinal("context")),
            reader.GetString(reader.GetOrdinal("file_name")),
            JsonSerializer.Deserialize<string[]>(reader.GetString(reader.GetOrdinal("arguments_json"))) ?? [],
            GetNullableString(reader, "working_directory"),
            reader.GetInt32(reader.GetOrdinal("timeout_ms")),
            reader.GetInt32(reader.GetOrdinal("terminal")) != 0,
            GetNullableInt(reader, "columns"),
            GetNullableInt(reader, "rows"),
            GetNullableInt(reader, "pid"),
            GetNullableString(reader, "effective_identity"),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_utc"))),
            GetNullableDateTimeOffset(reader, "started_utc"),
            GetNullableDateTimeOffset(reader, "completed_utc"),
            GetNullableInt(reader, "exit_code"),
            reader.GetInt32(reader.GetOrdinal("timed_out")) != 0,
            GetNullableString(reader, "failure_code"),
            GetNullableString(reader, "failure_message"),
            reader.GetString(reader.GetOrdinal("stdout_path")),
            reader.GetString(reader.GetOrdinal("stderr_path")));
    }

    private static string? GetNullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? GetNullableInt(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(SqliteDataReader reader, string name)
    {
        var value = GetNullableString(reader, name);
        return value is null ? null : DateTimeOffset.Parse(value);
    }

    private static void RequireUpdated(int count, string jobId)
    {
        if (count != 1)
            throw new ArgumentException($"Unknown job_id: {jobId}", nameof(jobId));
    }

    private static void EnsureProvider()
    {
        lock (ProviderGate)
        {
            if (ProviderInitialized)
                return;
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_winsqlite3());
            SQLitePCL.raw.FreezeProvider();
            ProviderInitialized = true;
        }
    }
}
