using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using StealthEye.Contract;

namespace Eye.Tests;

public sealed class DescriptorGenerationTests
{
    [Fact]
    public void PublishedOperations_AreOnlyCurrentHostOperations()
    {
        var contract = EyeContractCatalog.Load();

        Assert.Equal(
            ["capabilities", "run", "system.status"],
            contract.PublishedOperationIds.Order(StringComparer.Ordinal).ToArray());
        Assert.Empty(contract.AllowedEngineOperationIds);
        Assert.Equal("eye_inspect", contract.GetToolForOperation("system.status").Name);
        Assert.Equal("eye_run", contract.GetToolForOperation("run").Name);
    }

    [Fact]
    public void ImplementedToolDescriptors_AreGeneratedFromContract()
    {
        var descriptors = EyeDescriptorGenerator.GenerateImplemented(EyeContractCatalog.Load());

        Assert.Equal(["eye_inspect", "eye_run"], descriptors.Select(x => x.Name).ToArray());

        var inspect = descriptors.Single(x => x.Name == "eye_inspect");
        Assert.Equal(2, inspect.InputSchema.GetProperty("oneOf").GetArrayLength());
        Assert.Equal(4, inspect.OutputSchema.GetProperty("oneOf").GetArrayLength());

        var run = descriptors.Single(x => x.Name == "eye_run");
        Assert.Equal("run", run.InputSchema.GetProperty("properties").GetProperty("op").GetProperty("const").GetString());
        Assert.Contains("args", run.InputSchema.GetProperty("required").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(2, run.OutputSchema.GetProperty("oneOf").GetArrayLength());
    }

    [Fact]
    public void PublicDtos_MatchCurrentContractPropertySets()
    {
        var contract = EyeContractCatalog.Load();
        var status = Operation(contract, "system.status");
        var capabilities = Operation(contract, "capabilities");
        var run = Operation(contract, "run");

        AssertPropertySet<SystemStatusResult>(status.ResultSchema);
        AssertPropertySet<CapabilitiesResult>(capabilities.ResultSchema);
        AssertPropertySet<CapabilityFacades>(capabilities.ResultSchema.GetProperty("properties").GetProperty("facades"));
        AssertPropertySet<RunArgs>(run.ArgsSchema);
        Assert.Equal(
            ["system", "user", "wsl"],
            run.ArgsSchema.GetProperty("properties").GetProperty("context").GetProperty("enum")
                .EnumerateArray().Select(x => x.GetString()!).ToArray());
        AssertPropertySet<RunOperationResult>(run.ResultSchema);
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