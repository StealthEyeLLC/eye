using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using StealthEye.Runtime;

namespace StealthEye.Tools;

[McpServerToolType]
public sealed class EyeTool(EyeDispatcher dispatcher)
{
    [McpServerTool]
    [Description("Inspect local StealthEye machine state without intentionally mutating it.")]
    public Task<object> eye_inspect(
        [Description("Published inspect operation name.")] string op,
        [Description("Operation-specific arguments.")] JsonElement? args = null)
        => dispatcher.ExecuteAsync(EyeEffectClass.Inspect, op, args);

    [McpServerTool]
    [Description("Run a local Windows or WSL process in the requested execution context.")]
    public Task<object> eye_run(
        [Description("Published execution operation name.")] string op,
        [Description("Operation-specific arguments.")] JsonElement? args = null)
        => dispatcher.ExecuteAsync(EyeEffectClass.Run, op, args);

    [McpServerTool]
    [Description("Apply a precisely typed local machine, file, service, storage, or configuration change.")]
    public Task<object> eye_change(
        [Description("Published local-change operation name.")] string op,
        [Description("Operation-specific arguments.")] JsonElement? args = null)
        => dispatcher.ExecuteAsync(EyeEffectClass.Change, op, args);

    [McpServerTool]
    [Description("Interact with the active desktop, applications, or browser user interface.")]
    public Task<object> eye_interact(
        [Description("Published interactive operation name.")] string op,
        [Description("Operation-specific arguments.")] JsonElement? args = null)
        => dispatcher.ExecuteAsync(EyeEffectClass.Interact, op, args);

    [McpServerTool]
    [Description("Perform an operation whose intended effect leaves the local machine, such as sending, posting, uploading, or remote-provider administration.")]
    public Task<object> eye_external(
        [Description("Published external-effect operation name.")] string op,
        [Description("Operation-specific arguments.")] JsonElement? args = null)
        => dispatcher.ExecuteAsync(EyeEffectClass.External, op, args);
}
