using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using StealthEye.Contract;

namespace StealthEye.Tools;

public static class EyeGeneratedMcp
{
    public static IReadOnlyList<McpServerTool> CreateModelTools(EyeContractCatalog contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var descriptors = EyeDescriptorGenerator.GenerateImplemented(contract)
            .Where(x => !string.Equals(x.Name, "eye_live", StringComparison.Ordinal))
            .ToArray();

        var tools = new List<McpServerTool>(descriptors.Length);
        foreach (var descriptor in descriptors)
        {
            var method = typeof(EyeTool).GetMethod(
                descriptor.Name,
                BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException(
                    $"Generated MCP tool method '{descriptor.Name}' is missing.");

            var effectClass = contract.Descriptors
                .Single(x => string.Equals(x.Name, descriptor.Name, StringComparison.Ordinal))
                .EffectClass;

            var readOnly = string.Equals(effectClass, "inspect", StringComparison.Ordinal);
            var openWorld = string.Equals(effectClass, "external", StringComparison.Ordinal);

            var tool = McpServerTool.Create(
                method,
                context => (context.Services
                    ?? throw new InvalidOperationException("MCP request services are unavailable."))
                    .GetRequiredService<EyeTool>(),
                new McpServerToolCreateOptions
                {
                    Name = descriptor.Name,
                    Title = TitleFor(descriptor.Name),
                    Description = descriptor.Description,
                    ReadOnly = readOnly,
                    Destructive = !readOnly,
                    Idempotent = readOnly,
                    OpenWorld = openWorld,
                    UseStructuredContent = true,
                    OutputSchema = descriptor.OutputSchema
                });

            // Reflection remains only the invocation binding. The public protocol
            // surface itself comes from the canonical contract generator.
            tool.ProtocolTool.InputSchema = descriptor.InputSchema;
            tool.ProtocolTool.OutputSchema = descriptor.OutputSchema;
            tools.Add(tool);
        }

        return tools;
    }

    public static string NormalizeToolsList(IEnumerable<McpServerTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        return NormalizeProtocolTools(tools.Select(x => x.ProtocolTool));
    }

    public static string NormalizeProtocolTools(
        IEnumerable<ModelContextProtocol.Protocol.Tool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        var nodes = tools
            .Select(tool =>
            {
                var node = JsonNode.Parse(JsonSerializer.Serialize(tool))
                    ?? throw new InvalidOperationException(
                        $"Unable to serialize MCP tool '{tool.Name}'.");
                return Canonicalize(node);
            })
            .OrderBy(
                node => node?["name"]?.GetValue<string>(),
                StringComparer.Ordinal)
            .ToArray();

        return new JsonArray(nodes)
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true })
            .TrimEnd() + Environment.NewLine;
    }

    private static JsonNode Canonicalize(JsonNode node)
    {
        return node switch
        {
            JsonObject obj => new JsonObject(
                obj.OrderBy(x => x.Key, StringComparer.Ordinal)
                    .Select(x => KeyValuePair.Create(
                        x.Key,
                        x.Value is null ? null : Canonicalize(x.Value)))
                    .ToArray()),
            JsonArray array => new JsonArray(
                array.Select(x => x is null ? null : Canonicalize(x)).ToArray()),
            _ => node.DeepClone()
        };
    }

    private static string TitleFor(string name) => name switch
    {
        "eye_inspect" => "STEALTHEYE Inspect",
        "eye_run" => "STEALTHEYE Run",
        "eye_change" => "STEALTHEYE Change",
        "eye_interact" => "STEALTHEYE Interact",
        "eye_external" => "STEALTHEYE External",
        _ => name
    };
}
