namespace Eye.Tests;

public sealed class EyeOperatorGuidanceTests
{
    [Fact]
    public void OperatorSkillSource_TeachesCanonicalOperatingDoctrine()
    {
        var root = RepositoryRoot();
        var path = Path.Combine(root, "docs", "EYE_OPERATOR_SKILL.md");
        Assert.True(File.Exists(path), $"Missing Eye Operator skill source: {path}");

        var text = File.ReadAllText(path);
        Assert.Contains("Preferred modality hierarchy", text, StringComparison.Ordinal);
        Assert.Contains("job.wait", text, StringComparison.Ordinal);
        Assert.Contains("native waits/triggers", text, StringComparison.Ordinal);
        Assert.Contains("artifacts", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stable object ID + incarnation generation + observation cursor", text, StringComparison.Ordinal);
        Assert.Contains("Windows UI Automation", text, StringComparison.Ordinal);
        Assert.Contains("Chrome/CDP", text, StringComparison.Ordinal);
        Assert.Contains("Eye Live is optional", text, StringComparison.Ordinal);
        Assert.Contains("canonical contract", text, StringComparison.OrdinalIgnoreCase);
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