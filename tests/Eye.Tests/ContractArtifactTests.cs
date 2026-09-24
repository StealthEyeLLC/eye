using StealthEye.Contract;

namespace Eye.Tests;

public sealed class ContractArtifactTests
{
    [Fact]
    public void CanonicalContractHashAndReference_AreFrozenGeneratedArtifacts()
    {
        var contract = EyeContractCatalog.Load();
        var root = RepositoryRoot();
        var hashPath = Path.Combine(root, "contracts", "eye-mcp-v2.sha256");
        var referencePath = Path.Combine(root, "contracts", "eye-mcp-v2.reference.md");
        var reference = EyeContractDocumentation.GeneratePublicReference(contract);

        if (string.Equals(
            Environment.GetEnvironmentVariable("EYE_UPDATE_CONTRACT_ARTIFACTS"),
            "1",
            StringComparison.Ordinal))
        {
            File.WriteAllText(
                hashPath,
                contract.PublicContractHash + Environment.NewLine,
                new System.Text.UTF8Encoding(false));
            File.WriteAllText(
                referencePath,
                reference,
                new System.Text.UTF8Encoding(false));
        }

        Assert.True(File.Exists(hashPath), $"Missing frozen contract hash: {hashPath}");
        Assert.True(File.Exists(referencePath), $"Missing generated contract reference: {referencePath}");

        Assert.Equal(
            contract.PublicContractHash,
            File.ReadAllText(hashPath).Trim());

        Assert.Equal(
            File.ReadAllText(referencePath).Replace("\r\n", "\n", StringComparison.Ordinal),
            reference.Replace("\r\n", "\n", StringComparison.Ordinal));
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
