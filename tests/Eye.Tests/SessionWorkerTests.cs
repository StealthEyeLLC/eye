using System.Text;
using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

[Collection("Interactive desktop")]
public sealed class SessionWorkerTests
{
    [Theory]
    [InlineData("user")]
    [InlineData("wsl")]
    public async Task SessionWorker_RunsInteractiveTerminalInActiveSession(string context)
    {
        var workerExe = WorkerExecutable();
        var contract = EyeContractCatalog.Load();
        await using var worker = await SessionWorker.StartAsync(
            workerExe,
            contract.WorkerProtocolVersion,
            TimeSpan.FromSeconds(10));

        Assert.Equal(worker.ProcessId, worker.Handshake.ProcessId);
        Assert.Equal(contract.WorkerProtocolVersion, worker.Handshake.WorkerProtocolVersion);
        Assert.True(worker.Handshake.SessionId > 0);

        using var vt = new MemoryStream();
        using var copyCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var copyTask = worker.CopyVtToAsync(vt, copyCts.Token);
        var marker = "session-worker-" + context + "-ok";
        var started = await worker.StartTerminalAsync(new WorkerTerminalStartRequest(
            context,
            context == "wsl" ? "sh" : "cmd.exe",
            [],
            context == "wsl" ? "/mnt/x/repos/eye" : null,
            15_000,
            100,
            30));

        Assert.True(started.ProcessId > 0);
        Assert.Contains("StealthEye", started.EffectiveIdentity, StringComparison.OrdinalIgnoreCase);
        var resized = await worker.ResizeTerminalAsync(120, 40);
        Assert.Equal(120, resized.Columns);
        Assert.Equal(40, resized.Rows);

        if (context == "wsl")
            await worker.WriteTerminalAsync($"printf '{marker}:%s:%s\\n' \"$(id -u)\" \"$PWD\"\rexit\r");
        else
            await worker.WriteTerminalAsync($"echo {marker}\rexit\r");

        var exit = await worker.WaitTerminalAsync();
        Assert.Equal(0, exit.ExitCode);
        Assert.False(exit.TimedOut);
        await worker.StopAsync();
        await copyTask;

        var text = Encoding.UTF8.GetString(vt.ToArray());
        var compact = text.Replace("\r", "").Replace("\n", "");
        Assert.Contains(marker, compact, StringComparison.OrdinalIgnoreCase);
        if (context == "wsl")
            Assert.Contains(marker + ":0:/mnt/x/repos/eye", compact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionWorker_ObservesActiveSessionWindowInventory()
    {
        var contract = EyeContractCatalog.Load();
        await using var worker = await SessionWorker.StartAsync(
            WorkerExecutable(),
            contract.WorkerProtocolVersion,
            TimeSpan.FromSeconds(10));

        var observation = await worker.ObserveWindowsAsync();
        Assert.Equal(worker.Handshake.SessionId, observation.SessionId);
        Assert.NotEmpty(observation.Windows);
        Assert.All(observation.Windows, window =>
        {
            Assert.NotEqual(0, window.Hwnd);
            Assert.True(window.ProcessId > 0);
            Assert.True(window.Visible);
            Assert.False(string.IsNullOrWhiteSpace(window.ClassName));
            Assert.True(window.Bounds.Right >= window.Bounds.Left);
            Assert.True(window.Bounds.Bottom >= window.Bounds.Top);
        });
        Assert.Contains(observation.Windows, window => window.Foreground);
        Assert.Contains(observation.Windows, window =>
            window.Uia is { ControlType.Length: > 0 } &&
            !string.IsNullOrWhiteSpace(window.Uia.ClassName));
    }
    [Fact]
    public async Task SessionWorker_QueriesBoundedUiaTreeForObservedWindow()
    {
        var contract = EyeContractCatalog.Load();
        await using var worker = await SessionWorker.StartAsync(
            WorkerExecutable(),
            contract.WorkerProtocolVersion,
            TimeSpan.FromSeconds(10));

        var desktop = await worker.ObserveWindowsAsync();
        var target = desktop.Windows.First(window => window.Foreground && window.Uia is not null);
        var query = await worker.QueryUiaAsync(target.Hwnd, maxDepth: 3, maxNodes: 100);

        Assert.NotEmpty(query.Elements);
        Assert.True(query.Elements.Length <= 100);
        var root = query.Elements[0];
        Assert.Equal(0, root.Depth);
        Assert.Null(root.ParentRuntimeId);
        Assert.False(string.IsNullOrWhiteSpace(root.RuntimeId));
        Assert.False(string.IsNullOrWhiteSpace(root.ControlType));
        Assert.All(query.Elements, element => Assert.InRange(element.Depth, 0, 3));
        Assert.All(query.Elements.Skip(1), element => Assert.False(string.IsNullOrWhiteSpace(element.ParentRuntimeId)));
    }
    private static string WorkerExecutable()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Eye.slnx")))
            directory = directory.Parent;
        return Path.Combine(
            directory!.FullName,
            "src",
            "Eye.Worker",
            "bin",
            "Release",
            "net10.0-windows",
            "eye-worker.exe");
    }
}
