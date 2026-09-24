using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

namespace StealthEye.Contract;

public static class EyeRpcTransport
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
