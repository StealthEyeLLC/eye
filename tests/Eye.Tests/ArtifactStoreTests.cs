using System.Text;
using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class ArtifactStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eye-artifact-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Artifact_ImportInfoPreviewRangeExportDiffAndDelete()
    {
        Directory.CreateDirectory(_root);
        var leftPath = Path.Combine(_root, "left.txt");
        var rightPath = Path.Combine(_root, "right.txt");
        await File.WriteAllTextAsync(leftPath, "hello\nworld", new UTF8Encoding(false));
        await File.WriteAllTextAsync(rightPath, "hello\nWorld", new UTF8Encoding(false));

        var jobs = new JobStore(Path.Combine(_root, "state"), Path.Combine(_root, "spool", "jobs"));
        var artifacts = new ArtifactStore(jobs);
        var left = await artifacts.ImportFileAsync(leftPath, "text", "text/plain", "left.txt", "test");
        var right = await artifacts.ImportFileAsync(rightPath, "text", "text/plain", "right.txt", "test");

        Assert.StartsWith("artifact_", left.ArtifactId, StringComparison.Ordinal);
        Assert.Equal(1, left.Incarnation);
        Assert.Equal("hot", left.StorageTier);
        Assert.Equal(64, left.Sha256.Length);
        Assert.Equal(11, left.SizeBytes);
        Assert.Equal(left.ArtifactId, artifacts.Info(left.ArtifactId).ArtifactId);

        var preview = await artifacts.PreviewAsync(left.ArtifactId, 5);
        Assert.True(preview.TextAvailable);
        Assert.Equal("hello", preview.Text);
        Assert.True(preview.Truncated);

        var firstRange = await artifacts.ReadRangeAsync(left.ArtifactId, 0, 5);
        Assert.Equal("hello", Encoding.UTF8.GetString(Convert.FromBase64String(firstRange.DataBase64)));
        Assert.Equal(5, firstRange.NextOffset);
        Assert.False(firstRange.Eof);

        var diff = await artifacts.DiffAsync(left.ArtifactId, right.ArtifactId);
        Assert.False(diff.Equal);
        Assert.Equal(6, diff.FirstDifferenceOffset);

        var exportPath = Path.Combine(_root, "export", "copy.txt");
        await artifacts.ExportAsync(left.ArtifactId, exportPath, overwrite: false);
        Assert.Equal("hello\nworld", await File.ReadAllTextAsync(exportPath));

        Assert.True(artifacts.Delete(right.ArtifactId));
        Assert.Throws<ArgumentException>(() => artifacts.Info(right.ArtifactId));
    }

    [Fact]
    public async Task BinaryArtifact_HasNoTextPreview_AndRangeIsBounded()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "binary.bin");
        await File.WriteAllBytesAsync(source, Enumerable.Range(0, 256).Select(x => (byte)x).ToArray());
        var jobs = new JobStore(Path.Combine(_root, "state"), Path.Combine(_root, "spool", "jobs"));
        var artifacts = new ArtifactStore(jobs);
        var artifact = await artifacts.ImportFileAsync(source, "binary", "application/octet-stream", "binary.bin", "test");

        var preview = await artifacts.PreviewAsync(artifact.ArtifactId, 100);
        Assert.False(preview.TextAvailable);
        Assert.Null(preview.Text);

        var range = await artifacts.ReadRangeAsync(artifact.ArtifactId, 10, 20);
        Assert.Equal(20, range.BytesRead);
        Assert.Equal(30, range.NextOffset);
        Assert.False(range.Eof);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
