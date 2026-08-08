using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using StealthEye.Contract;

namespace Eye.Tests;

public sealed class DescriptorGenerationTests
{
    [Fact]
    public void PublishedOperations_AreOnlyImplementedHostOperations()
    {
        var contract = EyeContractCatalog.Load();

        Assert.Equal(
            [
                "artifact.delete", "artifact.diff", "artifact.export", "artifact.info", "artifact.preview", "artifact.read_range",
                "capabilities", "job.cancel", "job.read", "job.result", "job.start", "job.status", "job.wait", "run", "system.status"
            ],
            contract.PublishedOperationIds.Order(StringComparer.Ordinal).ToArray());
        Assert.Empty(contract.AllowedEngineOperationIds);
        Assert.Equal("eye_inspect", contract.GetToolForOperation("system.status").Name);
        Assert.Equal("eye_inspect", contract.GetToolForOperation("job.status").Name);
        Assert.Equal("eye_inspect", contract.GetToolForOperation("artifact.info").Name);
        Assert.Equal("eye_run", contract.GetToolForOperation("run").Name);
        Assert.Equal("eye_run", contract.GetToolForOperation("job.start").Name);
        Assert.Equal("eye_change", contract.GetToolForOperation("artifact.export").Name);
    }

    [Fact]
    public void ImplementedToolDescriptors_AreGeneratedFromContract()
    {
        var descriptors = EyeDescriptorGenerator.GenerateImplemented(EyeContractCatalog.Load());

        Assert.Equal(["eye_inspect", "eye_run", "eye_change"], descriptors.Select(x => x.Name).ToArray());

        var inspect = descriptors.Single(x => x.Name == "eye_inspect");
        Assert.Equal(10, inspect.InputSchema.GetProperty("oneOf").GetArrayLength());
        Assert.Equal(20, inspect.OutputSchema.GetProperty("oneOf").GetArrayLength());

        var run = descriptors.Single(x => x.Name == "eye_run");
        Assert.Equal(3, run.InputSchema.GetProperty("oneOf").GetArrayLength());
        Assert.Equal(6, run.OutputSchema.GetProperty("oneOf").GetArrayLength());
        Assert.Contains(
            "job.start",
            run.InputSchema.GetProperty("oneOf").EnumerateArray()
                .Select(x => x.GetProperty("properties").GetProperty("op").GetProperty("const").GetString()));

        var change = descriptors.Single(x => x.Name == "eye_change");
        Assert.Equal(2, change.InputSchema.GetProperty("oneOf").GetArrayLength());
        Assert.Equal(4, change.OutputSchema.GetProperty("oneOf").GetArrayLength());
    }

    [Fact]
    public void PublicDtos_MatchCurrentContractPropertySets()
    {
        var contract = EyeContractCatalog.Load();
        var systemStatus = Operation(contract, "system.status");
        var capabilities = Operation(contract, "capabilities");
        var run = Operation(contract, "run");
        var jobStart = Operation(contract, "job.start");
        var jobStatus = Operation(contract, "job.status");
        var jobRead = Operation(contract, "job.read");
        var jobWait = Operation(contract, "job.wait");
        var jobCancel = Operation(contract, "job.cancel");
        var jobResult = Operation(contract, "job.result");
        var artifactInfo = Operation(contract, "artifact.info");
        var artifactPreview = Operation(contract, "artifact.preview");
        var artifactRead = Operation(contract, "artifact.read_range");
        var artifactDiff = Operation(contract, "artifact.diff");
        var artifactExport = Operation(contract, "artifact.export");
        var artifactDelete = Operation(contract, "artifact.delete");

        AssertPropertySet<SystemStatusResult>(systemStatus.ResultSchema);
        AssertPropertySet<CapabilitiesResult>(capabilities.ResultSchema);
        AssertPropertySet<CapabilityFacades>(capabilities.ResultSchema.GetProperty("properties").GetProperty("facades"));
        AssertPropertySet<RunArgs>(run.ArgsSchema);
        Assert.Equal(2, run.ResultSchema.GetProperty("oneOf").GetArrayLength());
        AssertPropertySet<RunOperationResult>(run.ResultSchema.GetProperty("oneOf")[0]);
        AssertPropertySet<JobReferenceResult>(run.ResultSchema.GetProperty("oneOf")[1]);
        AssertPropertySet<RunArgs>(jobStart.ArgsSchema);
        AssertPropertySet<JobReferenceResult>(jobStart.ResultSchema);
        AssertPropertySet<JobIdArgs>(jobStatus.ArgsSchema);
        AssertPropertySet<JobStatusResult>(jobStatus.ResultSchema);
        AssertPropertySet<JobReadArgs>(jobRead.ArgsSchema);
        AssertPropertySet<JobReadPublicResult>(jobRead.ResultSchema);
        AssertPropertySet<JobWaitArgs>(jobWait.ArgsSchema);
        AssertPropertySet<JobWaitPublicResult>(jobWait.ResultSchema);
        AssertPropertySet<JobStatusResult>(jobWait.ResultSchema.GetProperty("properties").GetProperty("job"));
        AssertPropertySet<JobIdArgs>(jobCancel.ArgsSchema);
        AssertPropertySet<JobCancelResult>(jobCancel.ResultSchema);
        AssertPropertySet<JobIdArgs>(jobResult.ArgsSchema);
        AssertPropertySet<JobStatusResult>(jobResult.ResultSchema);
        AssertPropertySet<ArtifactIdArgs>(artifactInfo.ArgsSchema);
        AssertPropertySet<ArtifactInfoResult>(artifactInfo.ResultSchema);
        AssertPropertySet<ArtifactPreviewArgs>(artifactPreview.ArgsSchema);
        AssertPropertySet<ArtifactPreviewPublicResult>(artifactPreview.ResultSchema);
        AssertPropertySet<ArtifactReadRangeArgs>(artifactRead.ArgsSchema);
        AssertPropertySet<ArtifactReadRangeResult>(artifactRead.ResultSchema);
        AssertPropertySet<ArtifactDiffArgs>(artifactDiff.ArgsSchema);
        AssertPropertySet<ArtifactDiffPublicResult>(artifactDiff.ResultSchema);
        AssertPropertySet<ArtifactExportArgs>(artifactExport.ArgsSchema);
        AssertPropertySet<ArtifactExportResult>(artifactExport.ResultSchema);
        AssertPropertySet<ArtifactIdArgs>(artifactDelete.ArgsSchema);
        AssertPropertySet<ArtifactDeleteResult>(artifactDelete.ResultSchema);

        Assert.Equal(
            ["system", "user", "wsl"],
            run.ArgsSchema.GetProperty("properties").GetProperty("context").GetProperty("enum")
                .EnumerateArray().Select(x => x.GetString()!).ToArray());
    }

    [Fact]
    public void PublicContract_DoesNotExposeHostStorageInternalsOrUnimplementedTerminalOps()
    {
        var contract = EyeContractCatalog.Load();
        var serialized = JsonSerializer.Serialize(contract.Manifest);

        Assert.DoesNotContain("stdout_path", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("stderr_path", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("arguments_json", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("content_path", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("job.write", contract.PublishedOperationIds);
        Assert.DoesNotContain("job.resize", contract.PublishedOperationIds);
        Assert.DoesNotContain("job.attach", contract.PublishedOperationIds);
    }

    private static EyeOperationDescriptor Operation(EyeContractCatalog contract, string id) =>
        contract.Descriptors.SelectMany(x => x.Operations).Single(x => x.Id == id);

    private static void AssertPropertySet<T>(JsonElement schema)
    {
        var schemaNames = schema.GetProperty("properties").EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).ToArray();
        var dtoNames = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(x => x.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? x.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(schemaNames, dtoNames);
    }
}
