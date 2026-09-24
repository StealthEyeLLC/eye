using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using StealthEye.Contract;
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
    [Description("Run a local process in the requested execution context.")]
    public Task<object> eye_run(
        [Description("Published execution operation name.")] string op,
        [Description("Operation-specific arguments.")] JsonElement? args = null,
        [Description("Stable task identity for idempotent execution. Supply with action_id and postcondition.")] string? task_id = null,
        [Description("Stable action identity. Duplicate calls with the same inputs are not re-executed.")] string? action_id = null,
        [Description("Deterministic postcondition used to verify the effect.")] ActionPostconditionArgs? postcondition = null)
        => dispatcher.ExecuteAsync(
            EyeEffectClass.Run,
            op,
            args,
            BuildEnvelope(task_id, action_id, postcondition));

    [McpServerTool]
    [Description("Apply a precisely typed local machine, file, service, storage, or configuration change.")]
    public Task<object> eye_change(
        [Description("Published local-change operation name.")] string op,
        [Description("Operation-specific arguments.")] JsonElement? args = null,
        [Description("Stable task identity for idempotent execution. Supply with action_id and postcondition.")] string? task_id = null,
        [Description("Stable action identity. Duplicate calls with the same inputs are not re-executed.")] string? action_id = null,
        [Description("Deterministic postcondition used to verify the effect.")] ActionPostconditionArgs? postcondition = null)
        => dispatcher.ExecuteAsync(
            EyeEffectClass.Change,
            op,
            args,
            BuildEnvelope(task_id, action_id, postcondition));

    [McpServerTool]
    [Description("Interact with the active desktop, applications, or browser user interface.")]
    public Task<object> eye_interact(
        [Description("Published interactive operation name.")] string op,
        [Description("Operation-specific arguments.")] JsonElement? args = null,
        [Description("Stable task identity for idempotent execution. Supply with action_id and postcondition.")] string? task_id = null,
        [Description("Stable action identity. Duplicate calls with the same inputs are not re-executed.")] string? action_id = null,
        [Description("Deterministic postcondition used to verify the effect.")] ActionPostconditionArgs? postcondition = null)
        => dispatcher.ExecuteAsync(
            EyeEffectClass.Interact,
            op,
            args,
            BuildEnvelope(task_id, action_id, postcondition));

    [McpServerTool]
    [Description("Perform an operation whose intended effect leaves the local machine, such as sending, posting, uploading, or remote-provider administration.")]
    public Task<object> eye_external(
        [Description("Published external-effect operation name.")] string op,
        [Description("Operation-specific arguments.")] JsonElement? args = null,
        [Description("Stable task identity for idempotent execution. Supply with action_id and postcondition.")] string? task_id = null,
        [Description("Stable action identity. Duplicate calls with the same inputs are not re-executed.")] string? action_id = null,
        [Description("Deterministic postcondition used to verify the effect.")] ActionPostconditionArgs? postcondition = null)
        => dispatcher.ExecuteAsync(
            EyeEffectClass.External,
            op,
            args,
            BuildEnvelope(task_id, action_id, postcondition));

    private static ActionExecutionEnvelope? BuildEnvelope(
        string? taskId,
        string? actionId,
        ActionPostconditionArgs? postcondition)
    {
        if (string.IsNullOrWhiteSpace(taskId) &&
            string.IsNullOrWhiteSpace(actionId) &&
            postcondition is null)
            return null;

        return new ActionExecutionEnvelope(
            taskId,
            actionId,
            postcondition is null
                ? null
                : new ActionPostconditionContract(
                    postcondition.Kind,
                    postcondition.Spec.GetRawText()));
    }
}
