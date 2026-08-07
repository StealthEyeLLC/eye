using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using StealthEye.Runtime;

namespace StealthEye.Tools;

[McpServerToolType]
public sealed class EyeTool(EyeDispatcher dispatcher)
{
    [McpServerTool]
    [Description("Operate the dedicated StealthEye Windows machine through one stable operation surface.")]
    public Task<object> eye(
        [Description("Eye operation name.")] string op,
        [Description("Operation-specific arguments.")] JsonElement? args = null)
        => dispatcher.ExecuteAsync(op, args);
}
