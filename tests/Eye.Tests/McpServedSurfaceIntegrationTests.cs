using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ModelContextProtocol.Client;
using StealthEye.Contract;
using StealthEye.Runtime;
using StealthEye.Tools;

namespace Eye.Tests;

[Collection("Interactive desktop")]
public sealed class McpServedSurfaceIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "eye-mcp-surface-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LiveLoopbackServer_ServesFrozenGeneratedToolsListAndInstructions()
    {
        Directory.CreateDirectory(_root);
        var port = ReserveLoopbackPort();
        var baseUri = new Uri($"http://127.0.0.1:{port}");
        var hostAssembly = typeof(EyeDispatcher).Assembly.Location;
        var dotnet = ResolveDotnetHost();
        var stdout = new List<string>();
        var stderr = new List<string>();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(dotnet)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };
        process.StartInfo.ArgumentList.Add(hostAssembly);
        process.StartInfo.Environment["EYE_URLS"] = baseUri.ToString().TrimEnd('/');
        process.StartInfo.Environment["EYE_STATE_ROOT"] = Path.Combine(_root, "state");
        process.StartInfo.Environment["EYE_JOB_ROOT"] = Path.Combine(_root, "jobs");
        process.StartInfo.Environment["EYE_ENGINE_ROOT"] = Path.Combine(_root, "engines");
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                lock (stdout)
                    stdout.Add(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                lock (stderr)
                    stderr.Add(e.Data);
        };

        Assert.True(process.Start(), "Failed to launch temporary STEALTHEYE host.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await WaitForHealthAsync(baseUri, process, stdout, stderr);

            var options = new HttpClientTransportOptions
            {
                Endpoint = new Uri(baseUri, "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                ConnectionTimeout = TimeSpan.FromSeconds(10),
                Name = "eye-served-surface-test"
            };
            await using var transport = new HttpClientTransport(options);
            await using var client = await McpClient.CreateAsync(transport);

            var contract = EyeContractCatalog.Load();
            Assert.Equal(contract.Manifest.ServerInstructions, client.ServerInstructions);

            var tools = await client.ListToolsAsync();
            var appOnlyNames = EyeLiveMcp.CreateAppTools()
                .Select(x => x.ProtocolTool.Name)
                .ToHashSet(StringComparer.Ordinal);
            var expectedServedNames = contract.Descriptors
                .Select(x => x.Name)
                .Concat(appOnlyNames)
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(
                expectedServedNames,
                tools.Select(x => x.Name).Order(StringComparer.Ordinal).ToArray());

            Assert.Equal(
                contract.Descriptors.Select(x => x.Name).Order(StringComparer.Ordinal),
                tools.Where(x => !appOnlyNames.Contains(x.Name))
                    .Select(x => x.Name)
                    .Order(StringComparer.Ordinal));

            foreach (var appOnlyName in appOnlyNames)
            {
                var appOnly = tools.Single(x => x.Name == appOnlyName);
                var meta = appOnly.ProtocolTool.Meta!.ToJsonString()
                    .Replace(" ", string.Empty, StringComparison.Ordinal)
                    .Replace("\r", string.Empty, StringComparison.Ordinal)
                    .Replace("\n", string.Empty, StringComparison.Ordinal);
                Assert.Contains("\"visibility\":[\"app\"]", meta, StringComparison.Ordinal);
            }
            var actual = EyeGeneratedMcp.NormalizeProtocolTools(
                tools.Select(x => x.ProtocolTool));
            var expected = File.ReadAllText(Path.Combine(
                RepositoryRoot(),
                "contracts",
                "eye-mcp-v2.tools-list.normalized.json"));

            Assert.Equal(
                expected.Replace("\r\n", "\n", StringComparison.Ordinal),
                actual.Replace("\r\n", "\n", StringComparison.Ordinal));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    private static async Task WaitForHealthAsync(
        Uri baseUri,
        Process process,
        List<string> stdout,
        List<string> stderr)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        Exception? last = null;

        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
                throw new Xunit.Sdk.XunitException(
                    $"Temporary STEALTHEYE host exited with {process.ExitCode}.{Environment.NewLine}" +
                    $"stdout:{Environment.NewLine}{Snapshot(stdout)}{Environment.NewLine}" +
                    $"stderr:{Environment.NewLine}{Snapshot(stderr)}");

            try
            {
                using var response = await http.GetAsync(new Uri(baseUri, "/health"));
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (Exception ex) when (
                ex is HttpRequestException or TaskCanceledException)
            {
                last = ex;
            }

            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException(
            $"Temporary STEALTHEYE host did not become healthy: {last}{Environment.NewLine}" +
            $"stdout:{Environment.NewLine}{Snapshot(stdout)}{Environment.NewLine}" +
            $"stderr:{Environment.NewLine}{Snapshot(stderr)}");
    }

    private static string Snapshot(List<string> lines)
    {
        lock (lines)
            return string.Join(Environment.NewLine, lines.TakeLast(80));
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string ResolveDotnetHost()
    {
        var processPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
            return processPath;

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidate = Path.Combine(programFiles, "dotnet", "dotnet.exe");
        if (File.Exists(candidate))
            return candidate;

        throw new FileNotFoundException("Unable to locate dotnet.exe for MCP host integration test.");
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

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
