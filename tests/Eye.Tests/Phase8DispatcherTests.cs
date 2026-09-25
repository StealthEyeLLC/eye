using System.Text.Json;
using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

[Collection("Interactive desktop")]
public sealed class Phase8DispatcherTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "eye-phase8-dispatcher-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ManifestsMissionRelayChatAndContextFlowThroughSixToolDispatcher()
    {
        var jobs = new JobStore(
            Path.Combine(_root, "state"),
            Path.Combine(_root, "spool", "jobs"));
        var artifacts = new ArtifactStore(jobs);
        await using var workers = new SessionWorkerManager(
            WorkerExecutable(),
            WorkerRpcMethods.CurrentProtocolVersion);
        var windows = new DesktopWindowStore(jobs);
        var desktop = new DesktopObservationService(workers, windows);
        var uia = new UiaQueryService(windows, workers, new UiaElementStore(jobs));
        var captures = new DesktopCaptureService(jobs, windows, workers, artifacts);
        await using var browserSessions = new BrowserSessionManager(workers);
        var targets = new BrowserTargetStore(jobs);
        var browserObservation = new BrowserObservationService(browserSessions, targets);
        var browserControl = new BrowserControlService(
            browserSessions,
            targets,
            new BrowserDomStore(jobs),
            artifacts);
        var missions = new MissionBlackboardStore(jobs);
        var chats = new MissionChatAssociationStore(jobs);
        var relay = new RelayService(missions);
        var context = new ContextCaptureService(
            jobs,
            missions,
            desktop,
            uia,
            captures,
            browserObservation,
            artifacts,
            new DesktopContextService(workers));
        var continuation = new MissionContinuationService(context, relay);
        var manifests = new CapabilityManifestService();

        var dispatcher = new EyeDispatcher(
            new JobManager(jobs, new ProcessRunner()),
            artifacts,
            desktopObservationService: desktop,
            uiaQueryService: uia,
            browserObservationService: browserObservation,
            browserControlService: browserControl,
            publicContract: EyeContractCatalog.Load(),
            missionBlackboard: missions,
            relayService: relay,
            missionChats: chats,
            missionContinuation: continuation,
            capabilityManifests: manifests);

        var machine = Element(await dispatcher.ExecuteAsync(
            EyeEffectClass.Inspect,
            "machine.describe",
            null));
        Assert.True(machine.GetProperty("ok").GetBoolean(), machine.ToString());
        Assert.Equal(
            Environment.MachineName,
            machine.GetProperty("result").GetProperty("machine").GetString());

        var created = Element(await dispatcher.ExecuteAsync(
            EyeEffectClass.Change,
            "mission.create",
            JsonSerializer.SerializeToElement(
                new MissionCreateArgs("Finish Phase 8 through the public dispatcher."))));
        Assert.True(created.GetProperty("ok").GetBoolean(), created.ToString());
        var missionId = created.GetProperty("result").GetProperty("mission_id").GetString()!;

        var wrongTool = Element(await dispatcher.ExecuteAsync(
            EyeEffectClass.Inspect,
            "mission.create",
            JsonSerializer.SerializeToElement(new MissionCreateArgs("wrong tool"))));
        Assert.False(wrongTool.GetProperty("ok").GetBoolean());
        Assert.Equal("wrong_tool", wrongTool.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("eye_change", wrongTool.GetProperty("error").GetProperty("expected").GetProperty("tool").GetString());

        var associated = Element(await dispatcher.ExecuteAsync(
            EyeEffectClass.Change,
            "mission.chat_associate",
            JsonSerializer.SerializeToElement(new MissionChatAssociateArgs(
                missionId,
                "chat://phase8-test",
                "builder",
                false))));
        Assert.True(associated.GetProperty("ok").GetBoolean(), associated.ToString());

        var chatList = Element(await dispatcher.ExecuteAsync(
            EyeEffectClass.Inspect,
            "mission.chats",
            JsonSerializer.SerializeToElement(new MissionIdArgs(missionId))));
        Assert.True(chatList.GetProperty("ok").GetBoolean(), chatList.ToString());
        var association = Assert.Single(
            chatList.GetProperty("result").GetProperty("associations").EnumerateArray());
        Assert.Equal("chat://phase8-test", association.GetProperty("chat_ref").GetString());
        Assert.False(association.GetProperty("available").GetBoolean());

        var sent = Element(await dispatcher.ExecuteAsync(
            EyeEffectClass.Change,
            "relay.send",
            JsonSerializer.SerializeToElement(new RelaySendArgs(
                missionId,
                "phase8-test",
                "dispatcher relay survives chat availability"))));
        Assert.True(sent.GetProperty("ok").GetBoolean(), sent.ToString());

        var read = Element(await dispatcher.ExecuteAsync(
            EyeEffectClass.Inspect,
            "relay.read",
            JsonSerializer.SerializeToElement(new RelayReadArgs(missionId))));
        Assert.True(read.GetProperty("ok").GetBoolean(), read.ToString());
        Assert.Contains(
            read.GetProperty("result").GetProperty("messages").EnumerateArray(),
            x => x.GetProperty("message").GetString() == "dispatcher relay survives chat availability");

        var captured = Element(await dispatcher.ExecuteAsync(
            EyeEffectClass.Change,
            "context.capture",
            JsonSerializer.SerializeToElement(new ContextCaptureArgs(
                missionId,
                Source: "phase8-test",
                Note: "public dispatcher context capture"))));
        Assert.True(captured.GetProperty("ok").GetBoolean(), captured.ToString());
        var contextResult = captured.GetProperty("result").GetProperty("context");
        var artifactId = contextResult.GetProperty("artifact_id").GetString()!;
        Assert.StartsWith("artifact_", artifactId, StringComparison.Ordinal);
        var artifact = artifacts.Info(artifactId);
        Assert.Equal("context", artifact.Kind);
        var contextJson = await File.ReadAllTextAsync(artifact.ContentPath);
        Assert.Contains("\"selected_paths\"", contextJson, StringComparison.Ordinal);
        Assert.Contains("public dispatcher context capture", captured.ToString(), StringComparison.Ordinal);

        var operationList = Element(await dispatcher.ExecuteAsync(
            EyeEffectClass.Inspect,
            "operation.list",
            null));
        Assert.True(operationList.GetProperty("ok").GetBoolean(), operationList.ToString());
        Assert.Contains(
            operationList.GetProperty("result").GetProperty("operations").EnumerateArray(),
            x => x.GetProperty("name").GetString() == "windows.services" &&
                 x.GetProperty("available").GetBoolean());
    }

    private static JsonElement Element(object value) =>
        JsonSerializer.SerializeToElement(value);

    private static string WorkerExecutable()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Eye.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(
            directory!.FullName,
            "src",
            "Eye.Worker",
            "bin",
            "Release",
            "net10.0-windows",
            "eye-worker.exe");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}