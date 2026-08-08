using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using StealthEye.Contract;

var pipeName = RequiredArgument(args, "--pipe");
var contract = EyeContractCatalog.Load(typeof(Program).Assembly);
var engineVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
var handshake = new EngineHandshake(
    contract.EngineProtocolVersion,
    engineVersion,
    contract.PublicContractHash,
    contract.AllowedEngineOperationIds.Order(StringComparer.Ordinal).ToArray(),
    contract.WorkerProtocolVersion);

await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
await pipe.ConnectAsync(10_000);
using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };

while (true)
{
    var line = await reader.ReadLineAsync();
    if (line is null)
        break;

    EngineRpcRequest? request;
    try
    {
        request = JsonSerializer.Deserialize<EngineRpcRequest>(line);
    }
    catch (JsonException)
    {
        continue;
    }

    if (request is null || request.JsonRpc != "2.0")
        continue;

    object response;
    var stop = false;
    switch (request.Method)
    {
        case EngineRpcMethods.Handshake:
            response = new EngineRpcResponse<EngineHandshake>("2.0", request.Id, handshake);
            break;
        case EngineRpcMethods.Ping:
            response = new EngineRpcResponse<EnginePingResult>("2.0", request.Id, new EnginePingResult(engineVersion, Environment.ProcessId));
            break;
        case EngineRpcMethods.Shutdown:
            response = new EngineRpcResponse<EngineShutdownResult>("2.0", request.Id, new EngineShutdownResult(true));
            stop = true;
            break;
        default:
            response = new EngineRpcResponse<object>("2.0", request.Id, Error: new EngineRpcError("method_not_found", $"Unknown engine method: {request.Method}"));
            break;
    }

    await writer.WriteLineAsync(JsonSerializer.Serialize(response));
    if (stop)
        break;
}

static string RequiredArgument(string[] arguments, string name)
{
    for (var i = 0; i < arguments.Length - 1; i++)
    {
        if (string.Equals(arguments[i], name, StringComparison.Ordinal))
            return arguments[i + 1];
    }

    throw new ArgumentException($"Missing required argument: {name}");
}