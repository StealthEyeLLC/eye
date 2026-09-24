namespace Eye.Tests;

public sealed class McpBoundaryGuardTests
{
    [Fact]
    public void StableHost_DoesNotEnableSamplingPromptOrSecondBrainFeatures()
    {
        var root = FindRepositoryRoot();
        var sourceFiles = Directory
            .EnumerateFiles(Path.Combine(root, "src", "Eye"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var forbidden = new[]
        {
            ".WithPrompts(",
            "WithSampling(",
            "CreateMessageRequest",
            "SamplingMessage",
            "SamplingCreateMessage"
        };

        foreach (var sourceFile in sourceFiles)
        {
            var text = File.ReadAllText(sourceFile);
            foreach (var token in forbidden)
            {
                Assert.DoesNotContain(token, text, StringComparison.Ordinal);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Eye.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate Eye.slnx from test output directory.");
    }
}
