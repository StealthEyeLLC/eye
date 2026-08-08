using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace StealthEye.Runtime;

public sealed class ArtifactStore
{
    private readonly object _gate = new();
    private readonly string _connectionString;
    private readonly string _hotRoot;
    private readonly string? _coldRoot;

    public ArtifactStore(JobStore jobStore)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = jobStore.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();

        var spoolParent = Directory.GetParent(jobStore.SpoolRoot)?.FullName
            ?? throw new InvalidOperationException("Job spool root has no parent directory.");
        _hotRoot = Path.Combine(spoolParent, "artifacts");
        _coldRoot = Directory.Exists(@"E:\") ? @"E:\StealthEye\artifacts" : null;
        Directory.CreateDirectory(_hotRoot);
        Initialize();
    }

    public async Task<ArtifactRecord> ImportFileAsync(
        string sourcePath,
        string kind,
        string? mimeType,
        string? name,
        string provenance,
        string storageTier = "hot",
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
            throw new ArgumentException($"Artifact source file does not exist: {sourcePath}", nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(kind))
            throw new ArgumentException("kind is required.", nameof(kind));
        if (string.IsNullOrWhiteSpace(provenance))
            throw new ArgumentException("provenance is required.", nameof(provenance));

        var root = storageTier switch
        {
            "hot" => _hotRoot,
            "cold" when _coldRoot is not null => _coldRoot,
            "cold" => throw new InvalidOperationException("Cold artifact storage is unavailable because E: is not present."),
            _ => throw new ArgumentException("storage_tier must be hot or cold.", nameof(storageTier))
        };

        var artifactId = "artifact_" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(root, artifactId);
        Directory.CreateDirectory(directory);
        var contentPath = Path.Combine(directory, "content");
        var temporaryPath = contentPath + ".tmp";

        try
        {
            await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var target = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await source.CopyToAsync(target, cancellationToken);
            File.Move(temporaryPath, contentPath);

            var info = new FileInfo(contentPath);
            string hash;
            using (var stream = new FileStream(contentPath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.SequentialScan))
                hash = Convert.ToHexStringLower(SHA256.HashData(stream));

            var record = new ArtifactRecord(
                artifactId,
                1,
                kind,
                mimeType,
                info.Length,
                hash,
                string.IsNullOrWhiteSpace(name) ? Path.GetFileName(sourcePath) : name,
                storageTier,
                provenance,
                DateTimeOffset.UtcNow,
                contentPath);
            Insert(record);
            return record;
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            if (File.Exists(contentPath)) File.Delete(contentPath);
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            throw;
        }
    }

    public ArtifactRecord Info(string artifactId) => GetRequired(artifactId);

    public async Task<ArtifactPreviewResult> PreviewAsync(
        string artifactId,
        int maxChars,
        CancellationToken cancellationToken = default)
    {
        if (maxChars is < 1 or > 20_000)
            throw new ArgumentException("max_chars must be between 1 and 20000.", nameof(maxChars));

        var artifact = GetRequired(artifactId);
        if (!IsTextArtifact(artifact))
            return new ArtifactPreviewResult(artifactId, false, null, false);

        try
        {
            await using var stream = new FileStream(artifact.ContentPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            var buffer = new char[maxChars + 1];
            var count = 0;
            while (count < buffer.Length)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(count), cancellationToken);
                if (read == 0) break;
                count += read;
            }
            var truncated = count > maxChars;
            return new ArtifactPreviewResult(artifactId, true, new string(buffer, 0, Math.Min(count, maxChars)), truncated);
        }
        catch (DecoderFallbackException)
        {
            return new ArtifactPreviewResult(artifactId, false, null, false);
        }
    }

    public async Task<ArtifactRangeResult> ReadRangeAsync(
        string artifactId,
        long offset,
        int maxBytes,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0)
            throw new ArgumentException("offset must be non-negative.", nameof(offset));
        if (maxBytes is < 1 or > 262_144)
            throw new ArgumentException("max_bytes must be between 1 and 262144.", nameof(maxBytes));

        var artifact = GetRequired(artifactId);
        if (offset > artifact.SizeBytes)
            throw new ArgumentException($"offset {offset} exceeds artifact size {artifact.SizeBytes}.", nameof(offset));

        await using var stream = new FileStream(artifact.ContentPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.RandomAccess);
        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[(int)Math.Min(maxBytes, artifact.SizeBytes - offset)];
        var read = buffer.Length == 0 ? 0 : await stream.ReadAsync(buffer, cancellationToken);
        var next = offset + read;
        return new ArtifactRangeResult(
            artifactId,
            offset,
            read,
            next,
            next >= artifact.SizeBytes,
            Convert.ToBase64String(buffer, 0, read));
    }

    public async Task<ArtifactRecord> ExportAsync(
        string artifactId,
        string destination,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destination))
            throw new ArgumentException("destination is required.", nameof(destination));

        var artifact = GetRequired(artifactId);
        var fullDestination = Path.GetFullPath(destination);
        var parent = Path.GetDirectoryName(fullDestination)
            ?? throw new ArgumentException("destination must have a parent directory.", nameof(destination));
        Directory.CreateDirectory(parent);
        if (File.Exists(fullDestination) && !overwrite)
            throw new ArgumentException($"Destination already exists: {fullDestination}", nameof(destination));

        var temporary = fullDestination + ".eye-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var source = new FileStream(artifact.ContentPath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await source.CopyToAsync(target, cancellationToken);
            File.Move(temporary, fullDestination, overwrite);
            return artifact;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public bool Delete(string artifactId)
    {
        var artifact = GetRequired(artifactId);
        if (File.Exists(artifact.ContentPath))
            File.Delete(artifact.ContentPath);
        var directory = Path.GetDirectoryName(artifact.ContentPath);
        if (directory is not null && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            Directory.Delete(directory);

        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM artifacts WHERE artifact_id = $artifact_id;";
            command.Parameters.AddWithValue("$artifact_id", artifactId);
            return command.ExecuteNonQuery() == 1;
        }
    }

    public async Task<ArtifactDiffResult> DiffAsync(
        string leftArtifactId,
        string rightArtifactId,
        CancellationToken cancellationToken = default)
    {
        var left = GetRequired(leftArtifactId);
        var right = GetRequired(rightArtifactId);
        if (left.SizeBytes == right.SizeBytes && string.Equals(left.Sha256, right.Sha256, StringComparison.Ordinal))
            return new ArtifactDiffResult(leftArtifactId, rightArtifactId, true, left.SizeBytes, right.SizeBytes, left.Sha256, right.Sha256, null);

        long? firstDifference = null;
        await using var leftStream = new FileStream(left.ContentPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var rightStream = new FileStream(right.ContentPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var leftBuffer = new byte[65536];
        var rightBuffer = new byte[65536];
        long offset = 0;
        while (true)
        {
            var leftRead = await ReadChunkAsync(leftStream, leftBuffer, cancellationToken);
            var rightRead = await ReadChunkAsync(rightStream, rightBuffer, cancellationToken);
            var common = Math.Min(leftRead, rightRead);
            for (var i = 0; i < common; i++)
            {
                if (leftBuffer[i] == rightBuffer[i]) continue;
                firstDifference = offset + i;
                goto done;
            }

            if (leftRead != rightRead)
            {
                firstDifference = offset + common;
                break;
            }
            if (leftRead == 0)
                break;
            offset += leftRead;
        }

done:
        return new ArtifactDiffResult(leftArtifactId, rightArtifactId, false, left.SizeBytes, right.SizeBytes, left.Sha256, right.Sha256, firstDifference);
    }

    private static async Task<int> ReadChunkAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0) break;
            total += read;
        }
        return total;
    }
    private void Initialize()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS artifacts (
                    artifact_id TEXT PRIMARY KEY,
                    incarnation INTEGER NOT NULL,
                    kind TEXT NOT NULL,
                    mime_type TEXT NULL,
                    size_bytes INTEGER NOT NULL,
                    sha256 TEXT NOT NULL,
                    name TEXT NOT NULL,
                    storage_tier TEXT NOT NULL,
                    provenance TEXT NOT NULL,
                    created_utc TEXT NOT NULL,
                    content_path TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }
    }

    private void Insert(ArtifactRecord record)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO artifacts (
                    artifact_id, incarnation, kind, mime_type, size_bytes, sha256, name,
                    storage_tier, provenance, created_utc, content_path)
                VALUES ($artifact_id, $incarnation, $kind, $mime_type, $size_bytes, $sha256, $name,
                    $storage_tier, $provenance, $created_utc, $content_path);
                """;
            command.Parameters.AddWithValue("$artifact_id", record.ArtifactId);
            command.Parameters.AddWithValue("$incarnation", record.Incarnation);
            command.Parameters.AddWithValue("$kind", record.Kind);
            command.Parameters.AddWithValue("$mime_type", (object?)record.MimeType ?? DBNull.Value);
            command.Parameters.AddWithValue("$size_bytes", record.SizeBytes);
            command.Parameters.AddWithValue("$sha256", record.Sha256);
            command.Parameters.AddWithValue("$name", record.Name);
            command.Parameters.AddWithValue("$storage_tier", record.StorageTier);
            command.Parameters.AddWithValue("$provenance", record.Provenance);
            command.Parameters.AddWithValue("$created_utc", record.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("$content_path", record.ContentPath);
            command.ExecuteNonQuery();
        }
    }

    private ArtifactRecord GetRequired(string artifactId)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM artifacts WHERE artifact_id = $artifact_id;";
            command.Parameters.AddWithValue("$artifact_id", artifactId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                throw new ArgumentException($"Unknown artifact_id: {artifactId}", nameof(artifactId));
            return new ArtifactRecord(
                reader.GetString(reader.GetOrdinal("artifact_id")),
                reader.GetInt64(reader.GetOrdinal("incarnation")),
                reader.GetString(reader.GetOrdinal("kind")),
                GetNullableString(reader, "mime_type"),
                reader.GetInt64(reader.GetOrdinal("size_bytes")),
                reader.GetString(reader.GetOrdinal("sha256")),
                reader.GetString(reader.GetOrdinal("name")),
                reader.GetString(reader.GetOrdinal("storage_tier")),
                reader.GetString(reader.GetOrdinal("provenance")),
                DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_utc"))),
                reader.GetString(reader.GetOrdinal("content_path")));
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static string? GetNullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static bool IsTextArtifact(ArtifactRecord artifact)
    {
        if (artifact.Kind is "text" or "log" or "json" or "xml")
            return true;
        if (artifact.MimeType is null)
            return false;
        return artifact.MimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
               artifact.MimeType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
               artifact.MimeType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
               artifact.MimeType.Contains("javascript", StringComparison.OrdinalIgnoreCase);
    }
}
