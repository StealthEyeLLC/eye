using System.Diagnostics;

namespace Eye.Tests;

public sealed class CdpBindingGenerationTests
{
    [Fact]
    public async Task GeneratedCdpSubset_IsReproducibleFromRetainedSchema()
    {
        var root = RepositoryRoot();
        var script = Path.Combine(root, "tools", "generate-cdp-bindings.ps1");
        var spec = Path.Combine(root, "contracts", "cdp-bindings-subset.json");
        var expected = Path.Combine(root, "src", "Eye.Worker", "BrowserCdpBindings.Generated.cs");
        var temporary = Path.Combine(Path.GetTempPath(), "eye-cdp-generated-" + Guid.NewGuid().ToString("N") + ".cs");

        Assert.True(File.Exists(script), $"Missing generator: {script}");
        Assert.True(File.Exists(spec), $"Missing CDP subset schema: {spec}");
        Assert.True(File.Exists(expected), $"Missing generated binding file: {expected}");

        try
        {
            var start = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(script);
            start.ArgumentList.Add("-SpecPath");
            start.ArgumentList.Add(spec);
            start.ArgumentList.Add("-OutputPath");
            start.ArgumentList.Add(temporary);

            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Unable to start CDP generator.");
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.True(
                process.ExitCode == 0,
                $"CDP generator failed with {process.ExitCode}.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");

            Assert.Equal(
                await File.ReadAllTextAsync(expected),
                await File.ReadAllTextAsync(temporary));
        }
        finally
        {
            try { File.Delete(temporary); } catch { }
        }
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
}