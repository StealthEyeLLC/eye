using System.Text.Json;
using System.Text.Json.Serialization;

namespace StealthEye.Contract;

public static class EngineRpcMethods
{
    public const string Handshake = "engine.handshake";
    public const string Ping = "engine.ping";
    public const string Shutdown = "engine.shutdown";
}

public sealed record EngineRpcRequest(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Params = null);

public sealed record EngineRpcError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

public sealed record EngineRpcResponse<T>(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("result"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] T? Result = default,
    [property: JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EngineRpcError? Error = null);

public sealed record EnginePingResult(
    [property: JsonPropertyName("engine_version")] string EngineVersion,
    [property: JsonPropertyName("process_id")] int ProcessId);

public sealed record EngineShutdownResult(
    [property: JsonPropertyName("stopping")] bool Stopping);