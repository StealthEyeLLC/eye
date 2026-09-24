using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class EyeFoundationTortureTests
{
    [Fact]
    public async Task DisposableFoundationTortureSuite_Passes()
    {
        var report = await EyeFoundationTortureTest.RunAsync();
        Assert.Equal(DiagnosticStates.Pass, report.Overall);
        Assert.Equal(5, report.Cases.Length);
        Assert.All(report.Cases, item => Assert.Equal(DiagnosticStates.Pass, item.Status));
    }
}
