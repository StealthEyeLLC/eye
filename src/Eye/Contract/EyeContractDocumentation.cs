using System.Text;

namespace StealthEye.Contract;

public static class EyeContractDocumentation
{
    public static string GeneratePublicReference(EyeContractCatalog contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var manifest = contract.Manifest;
        var builder = new StringBuilder();
        builder.AppendLine("# STEALTHEYE MCP Public Contract Reference");
        builder.AppendLine();
        builder.AppendLine("> Generated from contracts/eye-mcp-v2.json. Do not edit by hand.");
        builder.AppendLine();
        builder.AppendLine($"- Contract: {manifest.Contract}");
        builder.AppendLine($"- Version: {manifest.Version}");
        builder.AppendLine($"- Status: {manifest.Status}");
        builder.AppendLine($"- Publication state: {manifest.PublicationState}");
        builder.AppendLine($"- Canonical JSON SHA-256: {contract.PublicContractHash}");
        builder.AppendLine($"- Host/engine protocol: {manifest.HostEngineProtocol.Version}");
        builder.AppendLine($"- Worker protocol: {manifest.HostEngineProtocol.WorkerProtocolVersion}");
        builder.AppendLine();
        builder.AppendLine("## Server instructions");
        builder.AppendLine();
        builder.AppendLine(manifest.ServerInstructions);
        builder.AppendLine();
        builder.AppendLine("## Public tools");
        builder.AppendLine();
        builder.AppendLine("| Tool | Effect class | Machine effects | Operations |");
        builder.AppendLine("| --- | --- | --- | --- |");

        foreach (var tool in manifest.Tools)
        {
            var operations = tool.Operations.Length == 0
                ? "(none)"
                : string.Join(", ", tool.Operations.Select(x => x.Id));
            builder.AppendLine(
                $"| {tool.Name} | {tool.EffectClass} | {tool.MachineEffects} | {operations} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Engine-owned public operations");
        builder.AppendLine();
        foreach (var operation in manifest.HostEngineProtocol.EngineOperationIds)
            builder.AppendLine($"- {operation}");

        builder.AppendLine();
        builder.AppendLine("## Runtime generation invariants");
        builder.AppendLine();
        builder.AppendLine("- Model-facing tool schemas are generated from the canonical contract.");
        builder.AppendLine("- tools/list is frozen by a normalized protocol snapshot.");
        builder.AppendLine("- capabilities projects operation membership from the canonical tool descriptors.");
        builder.AppendLine("- Server initialization instructions come from the canonical contract.");
        builder.AppendLine("- Host/engine compatibility is bound to the canonical contract hash.");
        return builder.ToString();
    }
}
