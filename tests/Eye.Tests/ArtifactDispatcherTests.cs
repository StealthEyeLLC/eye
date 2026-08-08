using System.Text;
using System.Text.Json;
using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class ArtifactDispatcherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eye-artifact-dispatcher-" + Guid.NewGuid().ToString("N"));
    private readonly ArtifactStore _artifacts;
    private readonly EyeDispatcher _dispatcher;

    public ArtifactDispatcherTests()
    {
        var jobs = new JobStore(Path.Combine(_root, "state"), Path.Combine(_root, "spool", "jobs"));
        _artifacts = new ArtifactStore(jobs);
        _dispatcher = new EyeDispatcher(new JobManager(jobs, new ProcessRunner()), _artifacts);
    }

    [Fact]
    public async Task Dispatcher_ArtifactReadOperations_AreBoundedAndPrivate()
    {
        Directory.CreateDirectory(_root);
        var leftPath = Path.Combine(_root, "left.txt");
        var rightPath = Path.Combine(_root, "right.txt");
        await File.WriteAllTextAsync(leftPath, "alpha\nbeta", new UTF8Encoding(false));
        await File.WriteAllTextAsync(rightPath, "alpha\nBeta", new UTF8Encoding(false));
        var left = await _artifacts.ImportFileAsync(leftPath, "text", "text/plain", "left.txt", "dispatcher-test");
        var right = await _artifacts.ImportFileAsync(rightPath, "text", "text/plain", "right.txt", "dispatcher-test");

        var info = Element(await _dispatcher.ExecuteAsync(
            EyeEffectClass.Inspect,
            "artifact.info",
            JsonSerializer.SerializeToElement(new ArtifactIdArgs(left.ArtifactId))));
        Assert.Equal("artifact.info", info.GetProperty("operation").GetString());
        Assert.Equal(left.ArtifactId, info.GetProperty("result").GetProperty("artifact_id").GetString());
        Assert.False(info.GetProperty("result").TryGetProperty("content_path", out _));

        var preview = Element(await _dispatcher.ExecuteAsync(
            EyeEffectClass.Inspect,
            "artifact.preview",
            JsonSerializer.SerializeToElement(new ArtifactPreviewArgs(left.ArtifactId, 5))));
        Assert.Equal("alpha", preview.GetProperty("result").GetProperty("text").GetString());
        Assert.True(preview.GetProperty("result").GetProperty("truncated").GetBoolean());

        var range = Element(await _dispatcher.ExecuteAsync(
            EyeEffectClass.Inspect,
            "artifact.read_range",
            JsonSerializer.SerializeToElement(new ArtifactReadRangeArgs(left.ArtifactId, 0, 5))));
        Assert.Equal("alpha", Encoding.UTF8.GetString(Convert.FromBase64String(range.GetProperty("result").GetProperty("data_base64").GetString()!)));

        var diff = Element(await _dispatcher.ExecuteAsync(
            EyeEffectClass.Inspect,
            "artifact.diff",
            JsonSerializer.SerializeToElement(new ArtifactDiffArgs(left.ArtifactId, right.ArtifactId))));
        Assert.False(diff.GetProperty("result").GetProperty("equal").GetBoolean());
        Assert.Equal(6, diff.GetProperty("result").GetProperty("first_difference_offset").GetInt64());
    }

    [Fact]
    public async Task Dispatcher_ArtifactExportAndDelete_BelongToEyeChange()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "source.txt");
        await File.WriteAllTextAsync(source, "export-me", new UTF8Encoding(false));
        var artifact = await _artifacts.ImportFileAsync(source, "text", "text/plain", "source.txt", "dispatcher-test");
        var export = Path.Combine(_root, "out", "copy.txt");

        var wrongTool = Element(await _dispatcher.ExecuteAsync(
            EyeEffectClass.Inspect,
            "artifact.export",
            JsonSerializer.SerializeToElement(new ArtifactExportArgs(artifact.ArtifactId, export))));
        Assert.Equal("wrong_tool", wrongTool.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("eye_change", wrongTool.GetProperty("error").GetProperty("expected").GetProperty("tool").GetString());

        var exported = Element(await _dispatcher.ExecuteAsync(
            EyeEffectClass.Change,
            "artifact.export",
            JsonSerializer.SerializeToElement(new ArtifactExportArgs(artifact.ArtifactId, export))));
        Assert.True(exported.GetProperty("ok").GetBoolean());
        Assert.Equal("export-me", await File.ReadAllTextAsync(export));

        var deleted = Element(await _dispatcher.ExecuteAsync(
            EyeEffectClass.Change,
            "artifact.delete",
            JsonSerializer.SerializeToElement(new ArtifactIdArgs(artifact.ArtifactId))));
        Assert.True(deleted.GetProperty("result").GetProperty("deleted").GetBoolean());
    }

    private static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
