using Newtonsoft.Json.Serialization;
using StreamJsonRpc;
using System.Text.Json.Serialization;

namespace StealthEye.Contract;

public static class EngineRpcMethods
{
    public const string Handshake = "engine.handshake";
    public const string Ping = "engine.ping";
    public const string Shutdown = "engine.shutdown";
}

public static class EngineRpcTransport
{
    public static IJsonRpcMessageHandler CreateMessageHandler(Stream stream)
    {
        var formatter = new JsonMessageFormatter();
        formatter.JsonSerializer.ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new SnakeCaseNamingStrategy()
        };
        return new HeaderDelimitedMessageHandler(stream, formatter);
    }
}

public sealed record EnginePingResult(
    [property: JsonPropertyName("engine_version")] string EngineVersion,
    [property: JsonPropertyName("process_id")] int ProcessId);

public sealed record EngineShutdownResult(
    [property: JsonPropertyName("stopping")] bool Stopping);
