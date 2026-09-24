using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Data.Sqlite;
using StealthEye.Contract;

namespace StealthEye.Runtime;

public static class DiagnosticStates
{
    public const string Pass = "PASS";
    public const string Warn = "WARN";
    public const string Fail = "FAIL";
}

public sealed record EyeDriveInventory(
    string Name,
    string DriveType,
    string? Format,
    long? TotalBytes,
    long? FreeBytes);

public sealed record EyeInventoryReport(
    DateTimeOffset CapturedAt,
    string Product,
    string Executable,
    string Version,
    string Machine,
    string Identity,
    string StateRoot,
    string SpoolRoot,
    string DatabasePath,
    string PublicContractHash,
    string[] PublicTools,
    long JobCount,
    long ArtifactCount,
    long ActionCount,
    long OutcomeUnknownActionCount,
    bool ActionLedgerValid,
    EyeDriveInventory[] Drives);

public sealed record EyeDoctorCheck(
    string Name,
    string Status,
    string Detail);

public sealed record EyeDoctorReport(
    DateTimeOffset CapturedAt,
    string Overall,
    EyeDoctorCheck[] Checks);

public sealed class EyeDiagnostics(
    JobStore jobs,
    ActionJournalStore actions,
    ArtifactStore artifacts,
    EyeContractCatalog contract)
{
    private const long HashVerificationLimitBytes = 64L * 1024L * 1024L;

    public EyeInventoryReport Inventory()
    {
        using var connection = Open();
        return new EyeInventoryReport(
            DateTimeOffset.UtcNow,
            "StealthEye",
            "eye",
            typeof(EyeDiagnostics).Assembly.GetName().Version?.ToString() ?? "unknown",
            Environment.MachineName,
            WindowsIdentity.GetCurrent().Name,
            jobs.StateRoot,
            jobs.SpoolRoot,
            jobs.DatabasePath,
            contract.PublicContractHash,
            contract.Descriptors.Select(x => x.Name).ToArray(),
            ScalarLong(connection, "SELECT COUNT(*) FROM jobs;"),
            ScalarLong(connection, "SELECT COUNT(*) FROM artifacts;"),
            ScalarLong(connection, "SELECT COUNT(*) FROM actions;"),
            ScalarLong(connection, "SELECT COUNT(*) FROM actions WHERE state = 'outcome_unknown';"),
            actions.VerifyLedger(),
            DriveInfo.GetDrives()
                .Select(ToDriveInventory)
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public async Task<EyeDoctorReport> DoctorAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<EyeDoctorCheck>();

        checks.Add(CheckContract());
        checks.Add(CheckPathWritable("state_root_writable", jobs.StateRoot));
        checks.Add(CheckPathWritable("spool_root_writable", jobs.SpoolRoot));

        using (var connection = Open())
        {
            checks.Add(CheckSqliteQuickCheck(connection));
            checks.Add(CheckWalMode(connection));
            checks.Add(CheckFts5(connection));

            var unknown = ScalarLong(
                connection,
                "SELECT COUNT(*) FROM actions WHERE state = 'outcome_unknown';");
            checks.Add(unknown == 0
                ? new EyeDoctorCheck(
                    "outcome_unknown_actions",
                    DiagnosticStates.Pass,
                    "No interrupted consequential actions require reconciliation.")
                : new EyeDoctorCheck(
                    "outcome_unknown_actions",
                    DiagnosticStates.Warn,
                    $"{unknown} action(s) require postcondition inspection before any retry."));
        }

        checks.Add(actions.VerifyLedger()
            ? new EyeDoctorCheck(
                "action_ledger_integrity",
                DiagnosticStates.Pass,
                "Action ledger hash chain is internally consistent.")
            : new EyeDoctorCheck(
                "action_ledger_integrity",
                DiagnosticStates.Fail,
                "Action ledger hash chain verification failed."));

        checks.Add(await CheckRecentArtifactsAsync(cancellationToken));

        var overall = checks.Any(x => x.Status == DiagnosticStates.Fail)
            ? DiagnosticStates.Fail
            : checks.Any(x => x.Status == DiagnosticStates.Warn)
                ? DiagnosticStates.Warn
                : DiagnosticStates.Pass;

        return new EyeDoctorReport(DateTimeOffset.UtcNow, overall, [.. checks]);
    }

    private EyeDoctorCheck CheckContract()
    {
        var names = contract.Descriptors.Select(x => x.Name).ToArray();
        string[] expected =
        [
            "eye_inspect",
            "eye_run",
            "eye_change",
            "eye_interact",
            "eye_external",
            "eye_live"
        ];

        var valid = names.SequenceEqual(expected, StringComparer.Ordinal)
            && contract.PublicContractHash.Length == 64;

        return valid
            ? new EyeDoctorCheck(
                "public_contract",
                DiagnosticStates.Pass,
                $"Frozen six-tool MCP contract validated; sha256={contract.PublicContractHash}.")
            : new EyeDoctorCheck(
                "public_contract",
                DiagnosticStates.Fail,
                "Public MCP contract does not match the frozen six-tool surface.");
    }

    private EyeDoctorCheck CheckSqliteQuickCheck(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        var result = Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
        return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase)
            ? new EyeDoctorCheck(
                "sqlite_quick_check",
                DiagnosticStates.Pass,
                "SQLite quick_check returned ok.")
            : new EyeDoctorCheck(
                "sqlite_quick_check",
                DiagnosticStates.Fail,
                $"SQLite quick_check returned '{result}'.");
    }

    private EyeDoctorCheck CheckWalMode(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        var result = Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
        return string.Equals(result, "wal", StringComparison.OrdinalIgnoreCase)
            ? new EyeDoctorCheck(
                "sqlite_wal",
                DiagnosticStates.Pass,
                "Canonical state database is using WAL journal mode.")
            : new EyeDoctorCheck(
                "sqlite_wal",
                DiagnosticStates.Fail,
                $"Expected WAL journal mode; observed '{result}'.");
    }

    private EyeDoctorCheck CheckFts5(SqliteConnection connection)
    {
        try
        {
            using (var create = connection.CreateCommand())
            {
                create.CommandText = "CREATE VIRTUAL TABLE temp.eye_fts5_probe USING fts5(content);";
                create.ExecuteNonQuery();
            }

            using (var drop = connection.CreateCommand())
            {
                drop.CommandText = "DROP TABLE temp.eye_fts5_probe;";
                drop.ExecuteNonQuery();
            }

            return new EyeDoctorCheck(
                "sqlite_fts5",
                DiagnosticStates.Pass,
                "SQLite FTS5 is available.");
        }
        catch (SqliteException ex)
        {
            return new EyeDoctorCheck(
                "sqlite_fts5",
                DiagnosticStates.Warn,
                $"SQLite FTS5 is unavailable: {ex.SqliteErrorCode} {ex.Message}");
        }
    }

    private static EyeDoctorCheck CheckPathWritable(string name, string root)
    {
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, ".eye-write-probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(path, [0x45, 0x59, 0x45]);
            File.Delete(path);
            return new EyeDoctorCheck(name, DiagnosticStates.Pass, $"{root} is writable.");
        }
        catch (Exception ex)
        {
            return new EyeDoctorCheck(
                name,
                DiagnosticStates.Fail,
                $"{root} is not safely writable: {ex.Message}");
        }
    }

    private async Task<EyeDoctorCheck> CheckRecentArtifactsAsync(CancellationToken cancellationToken)
    {
        ArtifactRecord[] recent;
        try
        {
            recent = artifacts.ListRecent(10);
        }
        catch (Exception ex)
        {
            return new EyeDoctorCheck(
                "recent_artifact_integrity",
                DiagnosticStates.Fail,
                $"Unable to enumerate recent artifacts: {ex.Message}");
        }

        var verifiedHashes = 0;
        foreach (var artifact in recent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(artifact.ContentPath))
            {
                return new EyeDoctorCheck(
                    "recent_artifact_integrity",
                    DiagnosticStates.Fail,
                    $"Artifact {artifact.ArtifactId} is registered but its content file is missing.");
            }

            var info = new FileInfo(artifact.ContentPath);
            if (info.Length != artifact.SizeBytes)
            {
                return new EyeDoctorCheck(
                    "recent_artifact_integrity",
                    DiagnosticStates.Fail,
                    $"Artifact {artifact.ArtifactId} size mismatch: database={artifact.SizeBytes}, file={info.Length}.");
            }

            if (info.Length <= HashVerificationLimitBytes)
            {
                await using var stream = new FileStream(
                    artifact.ContentPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    131072,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var hash = Convert.ToHexString(
                    await SHA256.HashDataAsync(stream, cancellationToken))
                    .ToLowerInvariant();
                if (!string.Equals(hash, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return new EyeDoctorCheck(
                        "recent_artifact_integrity",
                        DiagnosticStates.Fail,
                        $"Artifact {artifact.ArtifactId} SHA-256 mismatch.");
                }

                verifiedHashes++;
            }
        }

        return new EyeDoctorCheck(
            "recent_artifact_integrity",
            DiagnosticStates.Pass,
            $"Checked metadata for {recent.Length} recent artifact(s); recomputed {verifiedHashes} SHA-256 hash(es) within the bounded size limit.");
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = jobs.DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = false
            }.ToString());
        connection.Open();
        return connection;
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static EyeDriveInventory ToDriveInventory(DriveInfo drive)
    {
        try
        {
            if (!drive.IsReady)
                return new EyeDriveInventory(
                    drive.Name,
                    drive.DriveType.ToString(),
                    null,
                    null,
                    null);

            return new EyeDriveInventory(
                drive.Name,
                drive.DriveType.ToString(),
                drive.DriveFormat,
                drive.TotalSize,
                drive.AvailableFreeSpace);
        }
        catch
        {
            return new EyeDriveInventory(
                drive.Name,
                drive.DriveType.ToString(),
                null,
                null,
                null);
        }
    }
}
