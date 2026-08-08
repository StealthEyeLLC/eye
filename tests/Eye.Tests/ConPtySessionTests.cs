using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class ConPtySessionTests
{
    [Fact]
    public async Task SystemSession_WritesResizesAndCompletes()
    {
        await using var session = ConPtySession.Start(
            new RunRequest
            {
                Context = "system",
                FileName = "powershell.exe",
                Arguments = ["-NoLogo", "-NoProfile", "-NoExit"],
                TimeoutMs = 10000
            },
            80,
            25,
            hooks: null,
            CancellationToken.None);

        session.Resize(120, 40);
        Assert.Equal(120, session.Columns);
        Assert.Equal(40, session.Rows);

        await Task.Delay(250);
        var written = await session.WriteAsync("Write-Output eye-conpty-input\rexit\r");
        Assert.True(written > 0);

        var result = await session.Completion;
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Equal("system", result.Context);
        Assert.Contains("eye-conpty-input", result.Stdout, StringComparison.OrdinalIgnoreCase);
    }
}