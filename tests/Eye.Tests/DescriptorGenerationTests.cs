using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using StealthEye.Contract;
using StealthEye.Tools;

namespace Eye.Tests;

public sealed class DescriptorGenerationTests
{
    [Fact]
    public void PublishedOperations_AreOnlyImplementedHostOperations()
    {
        var contract = EyeContractCatalog.Load();

        Assert.Equal(
            [
                "action.status", "artifact.delete", "artifact.diff", "artifact.export", "artifact.info", "artifact.preview", "artifact.read_range",
                "browser.evaluate", "browser.navigate", "browser.observe", "capabilities", "engine.activate", "engine.restart", "engine.rollback", "engine.status", "job.attach", "job.cancel", "job.read",
                "job.resize", "job.result", "job.start", "job.status", "job.wait", "job.write", "run", "system.status", "ui.act", "ui.observe", "ui.query"
            ],
            contract.PublishedOperationIds.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(["browser.evaluate", "browser.navigate", "browser.observe", "ui.act", "ui.observe", "ui.query"], contract.AllowedEngineOperationIds.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("eye_inspect", contract.GetToolForOperation("system.status").Name);
        Assert.Equal("eye_inspect", contract.GetToolForOperation("action.status").Name);
        Assert.Equal("eye_inspect", contract.GetToolForOperation("engine.status").Name);
        Assert.Equal("eye_inspect", contract.GetToolForOperation("job.status").Name);
        Assert.Equal("eye_inspect", contract.GetToolForOperation("job.attach").Name);
        Assert.Equal("eye_inspect", contract.GetToolForOperation("artifact.info").Name);
        Assert.Equal("eye_inspect", contract.GetToolForOperation("ui.observe").Name);
        Assert.Equal("eye_inspect", contract.GetToolForOperation("ui.query").Name);
        Assert.Equal("eye_inspect", contract.GetToolForOperation("browser.observe").Name);
        Assert.Equal("eye_interact", contract.GetToolForOperation("ui.act").Name);
        Assert.Equal("eye_interact", contract.GetToolForOperation("browser.navigate").Name);
        Assert.Equal("eye_interact", contract.GetToolForOperation("browser.evaluate").Name);
        Assert.Equal("eye_run", contract.GetToolForOperation("run").Name);
        Assert.Equal("eye_run", contract.GetToolForOperation("job.start").Name);
        Assert.Equal("eye_run", contract.GetToolForOperation("job.write").Name);
        Assert.Equal("eye_run", contract.GetToolForOperation("job.resize").Name);
        Assert.Equal("eye_change", contract.GetToolForOperation("engine.activate").Name);
        Assert.Equal("eye_change", contract.GetToolForOperation("artifact.export").Name);
    }

    [Fact]
    public void ImplementedToolDescriptors_AreGeneratedFromContract()
    {
        var descriptors = EyeDescriptorGenerator.GenerateImplemented(EyeContractCatalog.Load());

        Assert.Equal(["eye_inspect", "eye_run", "eye_change", "eye_interact", "eye_live"], descriptors.Select(x => x.Name).ToArray());

        var inspect = descriptors.Single(x => x.Name == "eye_inspect");
        Assert.Equal(16, inspect.InputSchema.GetProperty("oneOf").GetArrayLength());
        Assert.Equal(32, inspect.OutputSchema.GetProperty("oneOf").GetArrayLength());

        var run = descriptors.Single(x => x.Name == "eye_run");
        Assert.Equal(5, run.InputSchema.GetProperty("oneOf").GetArrayLength());
        Assert.Equal(10, run.OutputSchema.GetProperty("oneOf").GetArrayLength());
        Assert.Contains(
            "job.start",
            run.InputSchema.GetProperty("oneOf").EnumerateArray()
                .Select(x => x.GetProperty("properties").GetProperty("op").GetProperty("const").GetString()));
        Assert.Contains(
            "job.write",
            run.InputSchema.GetProperty("oneOf").EnumerateArray()
                .Select(x => x.GetProperty("properties").GetProperty("op").GetProperty("const").GetString()));
        // Mutating facades support action envelopes without making them mandatory.
        foreach (var variant in run.InputSchema.GetProperty("oneOf").EnumerateArray())
        {
            var properties = variant.GetProperty("properties");
            Assert.True(properties.TryGetProperty("task_id", out _));
            Assert.True(properties.TryGetProperty("action_id", out _));
            Assert.True(properties.TryGetProperty("postcondition", out _));
            var required = variant.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToArray();
            Assert.DoesNotContain("task_id", required);
            Assert.DoesNotContain("action_id", required);
            Assert.DoesNotContain("postcondition", required);
        }

        var change = descriptors.Single(x => x.Name == "eye_change");
        Assert.Equal(5, change.InputSchema.GetProperty("oneOf").GetArrayLength());
        Assert.Equal(10, change.OutputSchema.GetProperty("oneOf").GetArrayLength());

        var interact = descriptors.Single(x => x.Name == "eye_interact");
        Assert.Equal(3, interact.InputSchema.GetProperty("oneOf").GetArrayLength());
        Assert.Equal(6, interact.OutputSchema.GetProperty("oneOf").GetArrayLength());
        Assert.Contains("ui.act", interact.InputSchema.GetProperty("oneOf").EnumerateArray().Select(x => x.GetProperty("properties").GetProperty("op").GetProperty("const").GetString()));
        Assert.Contains("browser.navigate", interact.InputSchema.GetProperty("oneOf").EnumerateArray().Select(x => x.GetProperty("properties").GetProperty("op").GetProperty("const").GetString()));
        Assert.Contains("browser.evaluate", interact.InputSchema.GetProperty("oneOf").EnumerateArray().Select(x => x.GetProperty("properties").GetProperty("op").GetProperty("const").GetString()));

        var live = descriptors.Single(x => x.Name == "eye_live");
        AssertPropertySet<EmptyArgs>(live.InputSchema);
        AssertPropertySet<EyeLiveSnapshotResult>(live.OutputSchema);
        Assert.Equal("ui://stealtheye/live", EyeContractCatalog.Load().Descriptors.Single(x => x.Name == "eye_live").ResourceUri);
    }

    [Fact]
    public void RuntimeProtocolTools_UseExactGeneratedSchemas()
    {
        var contract = EyeContractCatalog.Load();
        var generated = EyeDescriptorGenerator.GenerateImplemented(contract)
            .Where(x => x.Name != "eye_live")
            .ToDictionary(x => x.Name, StringComparer.Ordinal);
        var runtime = EyeGeneratedMcp.CreateModelTools(contract);

        Assert.Equal(
            generated.Keys.Order(StringComparer.Ordinal),
            runtime.Select(x => x.ProtocolTool.Name).Order(StringComparer.Ordinal));

        foreach (var tool in runtime)
        {
            var expected = generated[tool.ProtocolTool.Name];
            Assert.True(
                JsonElement.DeepEquals(expected.InputSchema, tool.ProtocolTool.InputSchema),
                $"Input schema drift for {tool.ProtocolTool.Name}.");
            Assert.True(
                tool.ProtocolTool.OutputSchema is JsonElement output &&
                JsonElement.DeepEquals(expected.OutputSchema, output),
                $"Output schema drift for {tool.ProtocolTool.Name}.");
        }
    }
    [Fact]
    public void RuntimeToolsList_MatchesFrozenNormalizedSnapshot()
    {
        var contract = EyeContractCatalog.Load();
        var tools = EyeGeneratedMcp.CreateModelTools(contract)
            .Append(EyeLiveMcp.CreateTool(contract))
            .ToArray();
        var actual = EyeGeneratedMcp.NormalizeToolsList(tools);
        var root = RepositoryRoot();
        var snapshotPath = Path.Combine(
            root,
            "contracts",
            "eye-mcp-v2.tools-list.normalized.json");

        if (string.Equals(
            Environment.GetEnvironmentVariable("EYE_UPDATE_CONTRACT_SNAPSHOT"),
            "1",
            StringComparison.Ordinal))
        {
            File.WriteAllText(snapshotPath, actual, new System.Text.UTF8Encoding(false));
        }

        Assert.True(File.Exists(snapshotPath), $"Missing tools/list snapshot: {snapshotPath}");
        var expected = File.ReadAllText(snapshotPath)
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(
            expected,
            actual.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void ServerInstructions_AreCanonicalAndFrontLoaded()
    {
        var instructions = EyeContractCatalog.Load().Manifest.ServerInstructions;

        Assert.False(string.IsNullOrWhiteSpace(instructions));
        Assert.True(instructions.Length <= 4000);
        var front = instructions[..Math.Min(512, instructions.Length)];
        Assert.Contains("ChatGPT", front, StringComparison.Ordinal);
        Assert.Contains("typed operations", front, StringComparison.Ordinal);
        Assert.Contains("durable jobs", front, StringComparison.Ordinal);
        Assert.Contains("artifact", front, StringComparison.Ordinal);
    }
    [Fact]
    public void PublicDtos_MatchCurrentContractPropertySets()
    {
        var contract = EyeContractCatalog.Load();
        var systemStatus = Operation(contract, "system.status");
        var actionStatus = Operation(contract, "action.status");
        var engineStatus = Operation(contract, "engine.status");
        var engineActivate = Operation(contract, "engine.activate");
        var engineRestart = Operation(contract, "engine.restart");
        var engineRollback = Operation(contract, "engine.rollback");
        var capabilities = Operation(contract, "capabilities");
        var run = Operation(contract, "run");
        var jobStart = Operation(contract, "job.start");
        var jobWrite = Operation(contract, "job.write");
        var jobResize = Operation(contract, "job.resize");
        var jobStatus = Operation(contract, "job.status");
        var jobRead = Operation(contract, "job.read");
        var jobWait = Operation(contract, "job.wait");
        var jobCancel = Operation(contract, "job.cancel");
        var jobResult = Operation(contract, "job.result");
        var jobAttach = Operation(contract, "job.attach");
        var artifactInfo = Operation(contract, "artifact.info");
        var artifactPreview = Operation(contract, "artifact.preview");
        var artifactRead = Operation(contract, "artifact.read_range");
        var artifactDiff = Operation(contract, "artifact.diff");
        var artifactExport = Operation(contract, "artifact.export");
        var artifactDelete = Operation(contract, "artifact.delete");
        var uiObserve = Operation(contract, "ui.observe");
        var uiQuery = Operation(contract, "ui.query");
        var uiAct = Operation(contract, "ui.act");
        var browserObserve = Operation(contract, "browser.observe");
        var browserNavigate = Operation(contract, "browser.navigate");
        var browserEvaluate = Operation(contract, "browser.evaluate");

        AssertPropertySet<SystemStatusResult>(systemStatus.ResultSchema);
        AssertPropertySet<ActionIdArgs>(actionStatus.ArgsSchema);
        AssertPropertySet<ActionStatusResult>(actionStatus.ResultSchema);
        AssertPropertySet<EmptyArgs>(engineStatus.ArgsSchema);
        AssertPropertySet<EngineStatusResult>(engineStatus.ResultSchema);
        AssertPropertySet<EngineActivateArgs>(engineActivate.ArgsSchema);
        AssertPropertySet<EngineStatusResult>(engineActivate.ResultSchema);
        AssertPropertySet<EmptyArgs>(engineRestart.ArgsSchema);
        AssertPropertySet<EngineStatusResult>(engineRestart.ResultSchema);
        AssertPropertySet<EmptyArgs>(engineRollback.ArgsSchema);
        AssertPropertySet<EngineStatusResult>(engineRollback.ResultSchema);
        AssertPropertySet<CapabilitiesResult>(capabilities.ResultSchema);
        AssertPropertySet<CapabilityFacades>(capabilities.ResultSchema.GetProperty("properties").GetProperty("facades"));
        AssertPropertySet<RunArgs>(run.ArgsSchema);
        Assert.Equal(2, run.ResultSchema.GetProperty("oneOf").GetArrayLength());
        AssertPropertySet<RunOperationResult>(run.ResultSchema.GetProperty("oneOf")[0]);
        AssertPropertySet<JobReferenceResult>(run.ResultSchema.GetProperty("oneOf")[1]);
        AssertPropertySet<JobStartArgs>(jobStart.ArgsSchema);
        AssertPropertySet<JobReferenceResult>(jobStart.ResultSchema);
        AssertPropertySet<JobWriteArgs>(jobWrite.ArgsSchema);
        AssertPropertySet<JobWriteResult>(jobWrite.ResultSchema);
        AssertPropertySet<JobResizeArgs>(jobResize.ArgsSchema);
        AssertPropertySet<JobResizeResult>(jobResize.ResultSchema);
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
        AssertPropertySet<JobIdArgs>(jobAttach.ArgsSchema);
        AssertPropertySet<JobAttachResult>(jobAttach.ResultSchema);
        AssertPropertySet<JobStatusResult>(jobAttach.ResultSchema.GetProperty("properties").GetProperty("job"));
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
        AssertPropertySet<UiObserveArgs>(uiObserve.ArgsSchema);
        AssertPropertySet<UiObserveResult>(uiObserve.ResultSchema);
        var uiProperties = uiObserve.ResultSchema.GetProperty("properties");
        var uiWindow = uiProperties.GetProperty("windows").GetProperty("items");
        AssertPropertySet<UiWindowResult>(uiWindow);
        AssertPropertySet<UiWindowBoundsResult>(uiWindow.GetProperty("properties").GetProperty("bounds"));
        AssertPropertySet<UiUiaRootResult>(uiWindow.GetProperty("properties").GetProperty("uia"));
        AssertPropertySet<UiQueryArgs>(uiQuery.ArgsSchema);
        AssertPropertySet<UiQueryResult>(uiQuery.ResultSchema);
        var uiElement = uiQuery.ResultSchema.GetProperty("properties").GetProperty("elements").GetProperty("items");
        AssertPropertySet<UiElementResult>(uiElement);
        AssertPropertySet<UiWindowBoundsResult>(uiElement.GetProperty("properties").GetProperty("bounds"));
        AssertPropertySet<UiActArgs>(uiAct.ArgsSchema);
        AssertPropertySet<UiActResult>(uiAct.ResultSchema);
        AssertPropertySet<EmptyArgs>(browserObserve.ArgsSchema);
        AssertPropertySet<BrowserObserveResult>(browserObserve.ResultSchema);
        AssertPropertySet<BrowserTargetResult>(browserObserve.ResultSchema.GetProperty("properties").GetProperty("targets").GetProperty("items"));
        AssertPropertySet<BrowserNavigateArgs>(browserNavigate.ArgsSchema);
        AssertPropertySet<BrowserNavigateResult>(browserNavigate.ResultSchema);
        AssertPropertySet<BrowserEvaluateArgs>(browserEvaluate.ArgsSchema);
        AssertPropertySet<BrowserEvaluateResult>(browserEvaluate.ResultSchema);
        var live = contract.Descriptors.Single(x => x.Name == "eye_live");
        AssertPropertySet<EmptyArgs>(live.InputSchema!.Value);
        AssertPropertySet<EyeLiveSnapshotResult>(live.ResultSchema!.Value);
        var liveProperties = live.ResultSchema.Value.GetProperty("properties");
        AssertPropertySet<EyeLiveMachineResult>(liveProperties.GetProperty("machine"));
        AssertPropertySet<EyeLiveEngineResult>(liveProperties.GetProperty("engine"));
        AssertPropertySet<EyeLiveJobResult>(liveProperties.GetProperty("jobs").GetProperty("items"));
        AssertPropertySet<EyeLiveTriggerResult>(liveProperties.GetProperty("triggers").GetProperty("items"));
        AssertPropertySet<EyeLiveArtifactResult>(liveProperties.GetProperty("artifacts").GetProperty("items"));

        Assert.Equal(
            ["system", "user", "wsl"],
            run.ArgsSchema.GetProperty("properties").GetProperty("context").GetProperty("enum")
                .EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.False(run.ArgsSchema.GetProperty("properties").TryGetProperty("terminal", out _));
        Assert.True(jobStart.ArgsSchema.GetProperty("properties").TryGetProperty("terminal", out var terminal));
        Assert.False(terminal.GetProperty("default").GetBoolean());
        Assert.Equal(120, jobStart.ArgsSchema.GetProperty("properties").GetProperty("columns").GetProperty("default").GetInt32());
        Assert.Equal(30, jobStart.ArgsSchema.GetProperty("properties").GetProperty("rows").GetProperty("default").GetInt32());
    }

    [Fact]
    public void PublicContract_DoesNotExposeHostStorageOrNativeHandleInternals()
    {
        var contract = EyeContractCatalog.Load();
        var serialized = JsonSerializer.Serialize(contract.Manifest);

        Assert.DoesNotContain("stdout_path", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("stderr_path", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("arguments_json", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("content_path", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("pseudo_console", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("native_handle", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("runtime_id", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cdp_target_id", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("debug_port", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("chrome_path", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user_data_dir", serialized, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Eye.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Unable to locate Eye repository root.");
    }
    private static EyeOperationDescriptor Operation(EyeContractCatalog contract, string id) =>
        contract.Descriptors.SelectMany(x => x.Operations).Single(x => x.Id == id);

    private static void AssertPropertySet<T>(JsonElement schema)
    {
        var schemaNames = schema.TryGetProperty("properties", out var properties)
            ? properties.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).ToArray()
            : [];
        var dtoNames = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(x => x.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? x.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(schemaNames, dtoNames);
    }
}
