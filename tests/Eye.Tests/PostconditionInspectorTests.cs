using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class PostconditionInspectorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "eye-postcondition-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FileInspector_NormalizesSizeAndHashEvidence()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "artifact.bin");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);

        var inspector = new FilePostconditionInspector();
        var result = await inspector.InspectAsync(
            System.Text.Json.JsonSerializer.Serialize(new
            {
                path,
                min_bytes = 4
            }));

        Assert.True(result.Satisfied);
        Assert.Contains("size_bytes", result.EvidenceJson, StringComparison.Ordinal);
        Assert.Contains("sha256", result.EvidenceJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileInspector_DoesNotTreatExistenceAloneAsSuccess()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "empty.bin");
        await File.WriteAllBytesAsync(path, []);

        var inspector = new FilePostconditionInspector();
        var result = await inspector.InspectAsync(
            System.Text.Json.JsonSerializer.Serialize(new
            {
                path,
                min_bytes = 1
            }));

        Assert.False(result.Satisfied);
        Assert.Equal("file_too_small", result.FailureCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}

