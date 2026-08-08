using StealthEye.Contract;

namespace Eye.Tests;

public sealed class ContractBoundaryTests
{
    [Fact]
    public void CanonicalV2Contract_LoadsFrozenSurfaceAndProtocol()
    {
        var contract = EyeContractCatalog.Load();

        Assert.Equal("stealtheye.eye.mcp", contract.Manifest.Contract);
        Assert.Equal("2.0.0", contract.Manifest.Version);
        Assert.Equal("canonical-target", contract.Manifest.Status);
        Assert.Equal("not-live-until-generated", contract.Manifest.PublicationState);
        Assert.Equal(
            ["eye_inspect", "eye_run", "eye_change", "eye_interact", "eye_external", "eye_live"],
            contract.Descriptors.Select(x => x.Name).ToArray());
        Assert.Equal("1.0.0", contract.EngineProtocolVersion);
        Assert.Equal("1.0.0", contract.WorkerProtocolVersion);
        Assert.Empty(contract.AllowedEngineOperationIds);
        Assert.Equal(64, contract.PublicContractHash.Length);
        Assert.Matches("^[0-9a-f]{64}$", contract.PublicContractHash);
    }

    [Fact]
    public void MatchingHandshake_IsCompatible()
    {
        var contract = EyeContractCatalog.Load();
        var handshake = MatchingHandshake(contract);

        Assert.Equal(EngineHandshakeValidation.Success, EngineHandshakeValidator.Validate(contract, handshake));
    }

    [Theory]
    [InlineData("protocol")]
    [InlineData("contract")]
    [InlineData("worker")]
    public void BoundaryMismatch_IsRejected(string mismatch)
    {
        var contract = EyeContractCatalog.Load();
        var handshake = MatchingHandshake(contract);
        handshake = mismatch switch
        {
            "protocol" => handshake with { EngineProtocolVersion = "9.0.0" },
            "contract" => handshake with { PublicContractHash = new string('0', 64) },
            "worker" => handshake with { WorkerProtocolVersion = "9.0.0" },
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch))
        };

        Assert.False(EngineHandshakeValidator.Validate(contract, handshake).Compatible);
    }

    [Fact]
    public void InvalidSupportedOperationIds_AreRejected()
    {
        var contract = EyeContractCatalog.Load();
        var handshake = MatchingHandshake(contract) with { SupportedOperationIds = ["desktop.observe", "desktop.observe"] };

        var result = EngineHandshakeValidator.Validate(contract, handshake);

        Assert.False(result.Compatible);
        Assert.Equal("invalid_supported_operation_ids", result.ErrorCode);
    }

    [Fact]
    public void UnpublishedEngineOperationId_IsRejected()
    {
        var contract = EyeContractCatalog.Load();
        var handshake = MatchingHandshake(contract) with { SupportedOperationIds = ["desktop.observe"] };

        var result = EngineHandshakeValidator.Validate(contract, handshake);

        Assert.False(result.Compatible);
        Assert.Equal("unsupported_operation_id", result.ErrorCode);
    }

    [Fact]
    public void ContractHash_IsWhitespaceInsensitive()
    {
        var compact = System.Text.Encoding.UTF8.GetBytes("{\"a\":1,\"b\":[true]}");
        var spaced = System.Text.Encoding.UTF8.GetBytes("{ \"a\" : 1, \"b\" : [ true ] }");

        Assert.Equal(
            EyeContractCatalog.ComputePublicContractHash(compact),
            EyeContractCatalog.ComputePublicContractHash(spaced));
    }
    private static EngineHandshake MatchingHandshake(EyeContractCatalog contract) => new(
        contract.EngineProtocolVersion,
        "phase-1-test-engine",
        contract.PublicContractHash,
        [],
        contract.WorkerProtocolVersion);
}