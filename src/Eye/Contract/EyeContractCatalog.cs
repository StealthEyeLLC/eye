using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StealthEye.Contract;

public sealed record EyeToolDescriptor(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("effect_class")] string EffectClass,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("machine_effects")] string MachineEffects,
    [property: JsonPropertyName("ui_only")] bool UiOnly = false);

public sealed record ContractHashSemantics(
    [property: JsonPropertyName("algorithm")] string Algorithm,
    [property: JsonPropertyName("encoding")] string Encoding,
    [property: JsonPropertyName("scope")] string Scope);

public sealed record EngineHandshakeShape(
    [property: JsonPropertyName("required")] string[] Required);

public sealed record HostEngineProtocolManifest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("transport")] string Transport,
    [property: JsonPropertyName("worker_protocol_version")] string WorkerProtocolVersion,
    [property: JsonPropertyName("engine_operation_ids")] string[] EngineOperationIds,
    [property: JsonPropertyName("contract_hash")] ContractHashSemantics ContractHash,
    [property: JsonPropertyName("engine_handshake")] EngineHandshakeShape EngineHandshake);

public sealed record EyeContractManifest(
    [property: JsonPropertyName("contract")] string Contract,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("tools")] EyeToolDescriptor[] Tools,
    [property: JsonPropertyName("host_engine_protocol")] HostEngineProtocolManifest HostEngineProtocol);

public sealed class EyeContractCatalog
{
    private const string ResourceName = "StealthEye.Contract.eye-mcp-v2.json";
    private static readonly string[] FrozenToolNames =
    [
        "eye_inspect",
        "eye_run",
        "eye_change",
        "eye_interact",
        "eye_external",
        "eye_live"
    ];

    private EyeContractCatalog(EyeContractManifest manifest, string publicContractHash)
    {
        Manifest = manifest;
        PublicContractHash = publicContractHash;
        AllowedEngineOperationIds = manifest.HostEngineProtocol.EngineOperationIds.ToHashSet(StringComparer.Ordinal);
    }

    public EyeContractManifest Manifest { get; }
    public string PublicContractHash { get; }
    public IReadOnlyList<EyeToolDescriptor> Descriptors => Manifest.Tools;
    public string EngineProtocolVersion => Manifest.HostEngineProtocol.Version;
    public string WorkerProtocolVersion => Manifest.HostEngineProtocol.WorkerProtocolVersion;
    public IReadOnlySet<string> AllowedEngineOperationIds { get; }

    public static EyeContractCatalog Load(Assembly? assembly = null)
    {
        assembly ??= typeof(EyeContractCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded contract resource: {ResourceName}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        var manifest = JsonSerializer.Deserialize<EyeContractManifest>(bytes)
            ?? throw new InvalidOperationException("Unable to deserialize the canonical v2 contract.");

        Validate(manifest);
        return new EyeContractCatalog(manifest, ComputePublicContractHash(bytes));
    }

    public static string ComputePublicContractHash(byte[] sourceBytes)
    {
        using var document = JsonDocument.Parse(sourceBytes);
        using var canonical = new MemoryStream();
        using (var writer = new Utf8JsonWriter(canonical))
            document.RootElement.WriteTo(writer);
        return Convert.ToHexStringLower(SHA256.HashData(canonical.ToArray()));
    }
    private static void Validate(EyeContractManifest manifest)
    {
        if (manifest.Contract != "stealtheye.eye.mcp" || manifest.Version != "2.0.0")
            throw new InvalidOperationException("Unexpected public contract identity or version.");
        if (manifest.Status != "canonical-target" || manifest.PublicationState != "not-live-until-generated")
            throw new InvalidOperationException("The v2 contract must remain a non-live canonical target during Phase 1.");
        if (!manifest.Tools.Select(x => x.Name).SequenceEqual(FrozenToolNames, StringComparer.Ordinal))
            throw new InvalidOperationException("The canonical v2 six-tool surface does not match the frozen tool names.");
        if (manifest.Tools.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != FrozenToolNames.Length)
            throw new InvalidOperationException("Duplicate public tool name in v2 contract.");
        if (manifest.HostEngineProtocol.Version != "1.0.0")
            throw new InvalidOperationException("Unsupported host/engine protocol version.");
        if (manifest.HostEngineProtocol.Transport != "stream-json-rpc-over-named-pipes")
            throw new InvalidOperationException("Unexpected host/engine control transport.");
        if (manifest.HostEngineProtocol.ContractHash is not { Algorithm: "sha256", Encoding: "lowercase-hex", Scope: "canonical-json-utf8" })
            throw new InvalidOperationException("Unexpected public contract hash semantics.");
        if (manifest.HostEngineProtocol.EngineOperationIds.Any(string.IsNullOrWhiteSpace) ||
            manifest.HostEngineProtocol.EngineOperationIds.Distinct(StringComparer.Ordinal).Count() != manifest.HostEngineProtocol.EngineOperationIds.Length)
            throw new InvalidOperationException("Invalid engine operation IDs in v2 contract.");

        string[] requiredHandshakeFields =
        [
            "engine_protocol_version",
            "engine_version",
            "public_contract_hash",
            "supported_operation_ids",
            "worker_protocol_version"
        ];
        if (!manifest.HostEngineProtocol.EngineHandshake.Required.SequenceEqual(requiredHandshakeFields, StringComparer.Ordinal))
            throw new InvalidOperationException("Unexpected engine handshake shape.");
    }
}