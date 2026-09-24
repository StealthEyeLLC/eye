using System.Security.Cryptography;
using System.Text.Json;

namespace StealthEye.Runtime;

public sealed record ActionExecutionEnvelope(
    string? TaskId,
    string? ActionId,
    ActionPostconditionContract? Postcondition)
{
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(TaskId) &&
        string.IsNullOrWhiteSpace(ActionId) &&
        Postcondition is null;
}

internal static class ActionInputHasher
{
    internal static string Compute(
        EyeEffectClass effectClass,
        string operation,
        JsonElement? args,
        ActionExecutionEnvelope envelope)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("effect_class", effectClass.ToString().ToLowerInvariant());
            writer.WriteString("operation", operation);
            writer.WriteString("task_id", envelope.TaskId);
            writer.WritePropertyName("args");
            if (args is null || args.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                writer.WriteNullValue();
            else
                WriteCanonical(writer, args.Value);

            writer.WriteString("postcondition_kind", envelope.Postcondition?.Kind);
            writer.WritePropertyName("postcondition_spec");
            if (envelope.Postcondition is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                using var spec = JsonDocument.Parse(envelope.Postcondition.SpecJson);
                WriteCanonical(writer, spec.RootElement);
            }

            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }
}
