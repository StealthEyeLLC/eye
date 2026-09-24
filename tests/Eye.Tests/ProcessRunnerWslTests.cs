using StealthEye.Runtime;

namespace Eye.Tests;

[Collection("Interactive desktop")]
public sealed class ProcessRunnerWslTests
{
    [Fact]
    public async Task SystemHost_WslRunsThroughInteractiveUserSession()
    {
        var runner = new ProcessRunner();
        var result = await runner.RunAsync(
            new RunRequest
            {
                Context = "wsl",
                FileName = "sh",
                Arguments = ["-lc", "whoami; printf '::'; uname -s"],
                WorkingDirectory = "/tmp",
                TimeoutMs = 20_000
            });

        Assert.False(result.TimedOut, result.Stderr);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("wsl", result.Context);
        Assert.Contains(@"\StealthEye", result.EffectiveIdentity, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("::Linux", result.Stdout, StringComparison.OrdinalIgnoreCase);
    }
}
