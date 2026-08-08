using System.Text.Json.Serialization;

namespace StealthEye.Contract;

public sealed record EngineHandshake(
    [property: JsonPropertyName("engine_protocol_version")] string EngineProtocolVersion,
    [property: JsonPropertyName("engine_version")] string EngineVersion,
    [property: JsonPropertyName("public_contract_hash")] string PublicContractHash,
    [property: JsonPropertyName("supported_operation_ids")] string[] SupportedOperationIds,
    [property: JsonPropertyName("worker_protocol_version")] string WorkerProtocolVersion);

public sealed record EngineHandshakeValidation(bool Compatible, string? ErrorCode = null)
{
    public static readonly EngineHandshakeValidation Success = new(true);
}

public static class EngineHandshakeValidator
{
    public static EngineHandshakeValidation Validate(EyeContractCatalog contract, EngineHandshake handshake)
    {
        if (!string.Equals(handshake.EngineProtocolVersion, contract.EngineProtocolVersion, StringComparison.Ordinal))
            return new(false, "engine_protocol_mismatch");
        if (string.IsNullOrWhiteSpace(handshake.EngineVersion))
            return new(false, "engine_version_missing");
        if (!string.Equals(handshake.PublicContractHash, contract.PublicContractHash, StringComparison.Ordinal))
            return new(false, "public_contract_hash_mismatch");
        if (!string.Equals(handshake.WorkerProtocolVersion, contract.WorkerProtocolVersion, StringComparison.Ordinal))
            return new(false, "worker_protocol_mismatch");
        if (handshake.SupportedOperationIds.Any(string.IsNullOrWhiteSpace) ||
            handshake.SupportedOperationIds.Distinct(StringComparer.Ordinal).Count() != handshake.SupportedOperationIds.Length)
            return new(false, "invalid_supported_operation_ids");
        if (handshake.SupportedOperationIds.Any(x => !contract.AllowedEngineOperationIds.Contains(x)))
            return new(false, "unsupported_operation_id");
        return EngineHandshakeValidation.Success;
    }
}