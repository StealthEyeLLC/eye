using System.Diagnostics;
using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class EngineInstanceTests
{
    [Fact]
    public async Task BuiltEngine_HandshakesPingsAndStops()
    {
        var executable = EngineExecutable();
        var contract = EyeContractCatalog.Load();
        var instance = await EngineInstance.StartAsync(executable, contract, TimeSpan.FromSeconds(10));
        var pid = instance.ProcessId;
        try
        {
            Assert.False(instance.HasExited);
            Assert.Equal(contract.EngineProtocolVersion, instance.Handshake.EngineProtocolVersion);
            Assert.Equal(contract.PublicContractHash, instance.Handshake.PublicContractHash);
            Assert.Equal(contract.WorkerProtocolVersion, instance.Handshake.WorkerProtocolVersion);
            Assert.Empty(instance.Handshake.SupportedOperationIds);
            Assert.Equal(pid, instance.Ping.ProcessId);

            var ping = await instance.CallAsync<EnginePingResult>(EngineRpcMethods.Ping);
            Assert.Equal(pid, ping.ProcessId);
            Assert.Equal(instance.Handshake.EngineVersion, ping.EngineVersion);
        }
        finally
        {
            await instance.DisposeAsync();
        }

        Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
    }

    [Fact]
    public async Task MissingEngine_IsRejectedBeforeLaunch()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            EngineInstance.StartAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "eye-engine.exe"), EyeContractCatalog.Load()));
    }

    private static string EngineExecutable()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Eye.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var path = Path.Combine(directory!.FullName, "src", "Eye.Engine", "bin", "Release", "net10.0-windows", "eye-engine.exe");
        Assert.True(File.Exists(path), $"Engine executable not found: {path}");
        return path;
    }
}