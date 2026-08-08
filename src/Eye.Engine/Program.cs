using System.IO.Pipes;
using StreamJsonRpc;
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
var target = new EngineRpcTarget(handshake, new EnginePingResult(engineVersion, Environment.ProcessId));
using var rpc = new JsonRpc(EngineRpcTransport.CreateMessageHandler(pipe), target);
rpc.StartListening();
try
{
    await rpc.Completion;
}
catch (ConnectionLostException)
{
    // The stable host closes the control pipe after the shutdown acknowledgement.
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

sealed class EngineRpcTarget(EngineHandshake handshake, EnginePingResult ping)
{
    [JsonRpcMethod(EngineRpcMethods.Handshake)]
    public EngineHandshake Handshake() => handshake;

    [JsonRpcMethod(EngineRpcMethods.Ping)]
    public EnginePingResult Ping() => ping;

    [JsonRpcMethod(EngineRpcMethods.Shutdown)]
    public EngineShutdownResult Shutdown() => new(true);
}
