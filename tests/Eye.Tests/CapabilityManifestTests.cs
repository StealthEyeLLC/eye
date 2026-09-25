using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class CapabilityManifestTests
{
    [Fact]
    public void ManifestsReportMeasuredMachineTruthAndOptionalAdapters()
    {
        var manifests = new CapabilityManifestService();

        var machine = manifests.DescribeMachine();
        Assert.Equal(Environment.MachineName, machine.Machine);
        Assert.True(machine.LogicalProcessors > 0);
        Assert.True(machine.MemoryTotalBytes > 0);
        Assert.True(machine.MemoryAvailableBytes >= 0);
        Assert.NotEmpty(machine.Volumes);
        Assert.Contains(machine.Software, x => x.Name == "git" && x.Available);
        Assert.Contains(machine.Software, x => x.Name == "powershell" && x.Available);
        Assert.Contains(machine.Software, x => x.Name == "wsl" && x.Available);
        Assert.Contains(machine.Operations, x => x.Name == "windows.services" && x.Available);
        Assert.Contains(machine.Operations, x => x.Name == "windows.task_scheduler" && x.Available);
        Assert.Contains(machine.Operations, x => x.Name == "windows.copyfile2" && x.Available);
        Assert.Contains(machine.Operations, x => x.Name == "docling" && x.OptionalExternal);

        var session = manifests.DescribeSession();
        Assert.True(session.HostSessionId >= 0);
        Assert.False(string.IsNullOrWhiteSpace(session.HostIdentity));

        var volumes = manifests.DescribeVolumes();
        Assert.Contains(
            volumes.Volumes,
            x => x.Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase) && x.Ready);
        var refsAvailable = volumes.Volumes.Any(x =>
            x.Ready &&
            string.Equals(x.Format, "ReFS", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            refsAvailable,
            manifests.OperationDescribe("windows.refs_clone").Operation.Available);

        var git = manifests.SoftwareVersion("git");
        Assert.True(git.Available);
        Assert.True(Path.IsPathFullyQualified(git.Path!));

        var duckdb = manifests.OperationDescribe("duckdb").Operation;
        Assert.Equal("data", duckdb.Category);
        Assert.Equal(
            manifests.FindSoftware("duckdb").Software.Single().Available,
            duckdb.Available);
    }
}