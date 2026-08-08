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
            .Where(x => x.Operations.Length > 0)
            .Select(x => new GeneratedToolDescriptor(
                x.Name,
                x.Description,
                ToElement(BuildInputSchema(x)),
                ToElement(BuildOutputSchema(contract, x))))
            .ToArray();

    private static JsonNode BuildInputSchema(EyeToolDescriptor tool)
    {
        var variants = tool.Operations.Select(operation =>
        {
            var properties = new JsonObject
            {
                ["op"] = new JsonObject { ["const"] = operation.Id },
                ["args"] = Clone(operation.ArgsSchema)
            };
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
            : new JsonObject { ["oneOf"] = new JsonArray(variants) };
    }

    private static JsonNode BuildOutputSchema(EyeContractCatalog contract, EyeToolDescriptor tool)
    {
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

        return new JsonObject { ["oneOf"] = new JsonArray(variants.ToArray()) };
    }

    private static JsonNode Clone(JsonElement element) =>
        JsonNode.Parse(element.GetRawText()) ?? throw new InvalidOperationException("Invalid contract schema node.");

    private static JsonElement ToElement(JsonNode node) =>
        JsonSerializer.SerializeToElement(node);
}