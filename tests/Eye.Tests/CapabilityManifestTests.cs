using System.Text;
using System.Text.Json;
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

    [Fact]
    public void SuiteCatalogExtendsOperationManifestsWithoutChangingPublicTools()
    {
        var root = Path.Combine(Path.GetTempPath(), "eye-suite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var localApp = Path.Combine(root, "studio");
        Directory.CreateDirectory(localApp);
        var missingApp = Path.Combine(root, "missing");
        var catalog = Path.Combine(root, "suite-capabilities.json");

        var payload = new
        {
            apps = new object[]
            {
                new
                {
                    id = "studio",
                    manifest_name = "suite.studio",
                    category = "image-video",
                    provider = "STEALTHEYE Suite",
                    authority = "eye",
                    optional_external = false,
                    available_local_path = localApp,
                    summary = "Local Studio recipe."
                },
                new
                {
                    id = "missing",
                    manifest_name = "suite.missing",
                    category = "suite",
                    provider = "STEALTHEYE Suite",
                    authority = "eye",
                    optional_external = false,
                    available_local_path = missingApp,
                    summary = "Missing local recipe."
                },
                new
                {
                    id = "remote",
                    manifest_name = "suite.remote",
                    category = "device-control",
                    provider = "STEALTHEYE Suite",
                    authority = "StealthEye Desktop",
                    optional_external = true,
                    configured = true,
                    summary = "Desktop-owned recipe."
                }
            }
        };

        File.WriteAllText(catalog, JsonSerializer.Serialize(payload), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            var manifests = new CapabilityManifestService(catalog);
            var list = manifests.OperationList().Operations;

            var studio = Assert.Single(list, x => x.Name == "suite.studio");
            Assert.True(studio.Available);
            Assert.False(studio.OptionalExternal);
            Assert.Contains("Authority=eye", studio.Detail, StringComparison.Ordinal);
            Assert.Contains(catalog, studio.Detail, StringComparison.Ordinal);

            var missing = Assert.Single(list, x => x.Name == "suite.missing");
            Assert.False(missing.Available);

            var remote = Assert.Single(list, x => x.Name == "suite.remote");
            Assert.True(remote.Available);
            Assert.True(remote.OptionalExternal);
            Assert.Contains("Authority=StealthEye Desktop", remote.Detail, StringComparison.Ordinal);

            Assert.Equal(studio, manifests.OperationDescribe("suite.studio").Operation);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }}