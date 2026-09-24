using System.Text.Json;
using System.Text.Json.Nodes;

namespace StealthEye.Contract;

public sealed record GeneratedToolDescriptor(
    string Name,
    string Description,
    JsonElement InputSchema,
    JsonElement OutputSchema);

public static class EyeDescriptorGenerator
{
    public static IReadOnlyList<GeneratedToolDescriptor> GenerateImplemented(EyeContractCatalog contract) =>
        contract.Descriptors
            .Where(x => x.Operations.Length > 0 || x.UiOnly)
            .Select(x => new GeneratedToolDescriptor(
                x.Name,
                x.Description,
                ToElement(BuildInputSchema(x)),
                ToElement(BuildOutputSchema(contract, x))))
            .ToArray();

    private static JsonNode BuildInputSchema(EyeToolDescriptor tool)
    {
        if (tool.UiOnly)
            return Clone(tool.InputSchema ?? throw new InvalidOperationException("UI tool input schema is missing."));

        var variants = tool.Operations.Select(operation =>
        {
            var properties = new JsonObject
            {
                ["op"] = new JsonObject { ["const"] = operation.Id },
                ["args"] = Clone(operation.ArgsSchema)
            };
            if (tool.SupportsActionEnvelope)
            {
                properties["task_id"] = new JsonObject
                {
                    ["type"] = "string",
                    ["minLength"] = 1,
                    ["maxLength"] = 256
                };
                properties["action_id"] = new JsonObject
                {
                    ["type"] = "string",
                    ["minLength"] = 1,
                    ["maxLength"] = 256
                };
                properties["postcondition"] = BuildPostconditionSchema();
            }
            var required = new JsonArray("op");
            if (operation.ArgsRequired)
                required.Add("args");

            return (JsonNode)new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required,
                ["additionalProperties"] = false
            };
        }).ToArray();

        return variants.Length == 1
            ? variants[0]
            : new JsonObject
            {
                ["type"] = "object",
                ["oneOf"] = new JsonArray(variants)
            };
    }

    private static JsonNode BuildPostconditionSchema() => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["kind"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray("file", "command")
            },
            ["spec"] = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = true
            }
        },
        ["required"] = new JsonArray("kind", "spec"),
        ["additionalProperties"] = false
    };
    private static JsonNode BuildOutputSchema(EyeContractCatalog contract, EyeToolDescriptor tool)
    {
        if (tool.UiOnly)
            return Clone(tool.ResultSchema ?? throw new InvalidOperationException("UI tool result schema is missing."));

        var variants = new List<JsonNode>();
        foreach (var operation in tool.Operations)
        {
            variants.Add(new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["ok"] = new JsonObject { ["const"] = true },
                    ["operation"] = new JsonObject { ["const"] = operation.Id },
                    ["result"] = Clone(operation.ResultSchema)
                },
                ["required"] = new JsonArray("ok", "operation", "result"),
                ["additionalProperties"] = false
            });
            variants.Add(new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["ok"] = new JsonObject { ["const"] = false },
                    ["operation"] = new JsonObject { ["const"] = operation.Id },
                    ["error"] = Clone(contract.Manifest.ErrorSchema)
                },
                ["required"] = new JsonArray("ok", "operation", "error"),
                ["additionalProperties"] = false
            });
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["oneOf"] = new JsonArray(variants.ToArray())
        };
    }

    private static JsonNode Clone(JsonElement element) =>
        JsonNode.Parse(element.GetRawText()) ?? throw new InvalidOperationException("Invalid contract schema node.");

    private static JsonElement ToElement(JsonNode node) =>
        JsonSerializer.SerializeToElement(node);
}
