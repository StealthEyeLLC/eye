using System.Text.Json;
using System.Text.Json.Serialization;

namespace StealthEye.Runtime;

public sealed record ConsequentialActionRequest(
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("action_id")] string ActionId,
    [property: JsonPropertyName("capability")] string Capability,
    [property: JsonPropertyName("input_sha256")] string InputSha256,
    [property: JsonPropertyName("postcondition")] ActionPostconditionContract Postcondition);

public sealed record ConsequentialActionResult(
    [property: JsonPropertyName("disposition")] ActionReservationDisposition Disposition,
    [property: JsonPropertyName("action")] ActionRecord Action,
    [property: JsonPropertyName("executor_invoked")] bool ExecutorInvoked,
    [property: JsonPropertyName("postcondition_satisfied")] bool? PostconditionSatisfied,
    [property: JsonPropertyName("evidence_json")] string? EvidenceJson,
    [property: JsonPropertyName("execution_error")] string? ExecutionError);

public sealed class ConsequentialActionRunner(
    ActionJournalStore actions,
    PostconditionInspectorRegistry inspectors)
{
    public async Task<ConsequentialActionResult> ExecuteAsync(
        ConsequentialActionRequest request,
        Func<CancellationToken, Task<object?>> executor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Postcondition);
        ArgumentNullException.ThrowIfNull(executor);

        if (!inspectors.TryGet(request.Postcondition.Kind, out var inspector))
            throw new InvalidOperationException(
                $"No postcondition inspector is registered for kind '{request.Postcondition.Kind}'.");

        var reservation = actions.Reserve(
            request.TaskId,
            request.ActionId,
            request.Capability,
            request.InputSha256,
            request.Postcondition);

        if (reservation.Disposition != ActionReservationDisposition.Execute)
        {
            return new ConsequentialActionResult(
                reservation.Disposition,
                reservation.Action,
                false,
                reservation.Action.State == ActionStates.Verified ? true : null,
                reservation.Action.EvidenceJson,
                null);
        }

        // Both durable transitions happen before user code is allowed to produce a side effect.
        actions.MarkDispatching(request.ActionId);
        actions.MarkRunning(request.ActionId);

        string? resultJson = null;
        try
        {
            var result = await executor(cancellationToken);
            resultJson = JsonSerializer.Serialize(result);

            var inspection = await inspector.InspectAsync(
                request.Postcondition.SpecJson,
                cancellationToken);
            if (inspection.Satisfied)
            {
                var verified = actions.MarkVerified(
                    request.ActionId,
                    inspection.EvidenceJson,
                    resultJson);
                return new ConsequentialActionResult(
                    ActionReservationDisposition.Execute,
                    verified,
                    true,
                    true,
                    inspection.EvidenceJson,
                    null);
            }

            var failed = actions.MarkFailed(
                request.ActionId,
                inspection.EvidenceJson,
                resultJson);
            return new ConsequentialActionResult(
                ActionReservationDisposition.Execute,
                failed,
                true,
                false,
                inspection.EvidenceJson,
                null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            PostconditionInspection? inspection = null;
            try
            {
                inspection = await inspector.InspectAsync(
                    request.Postcondition.SpecJson,
                    CancellationToken.None);
            }
            catch
            {
                // If inspection itself fails, preserve uncertainty rather than inventing a failure result.
            }

            if (inspection?.Satisfied == true)
            {
                var verified = actions.MarkVerified(
                    request.ActionId,
                    inspection.EvidenceJson,
                    JsonSerializer.Serialize(new
                    {
                        executor_exception = ex.GetType().FullName,
                        executor_message = ex.Message,
                        result_json = resultJson
                    }));
                return new ConsequentialActionResult(
                    ActionReservationDisposition.Execute,
                    verified,
                    true,
                    true,
                    inspection.EvidenceJson,
                    ex.Message);
            }

            var uncertaintyEvidence = inspection?.EvidenceJson ?? JsonSerializer.Serialize(new
            {
                postcondition_inspection = "unavailable",
                executor_exception = ex.GetType().FullName,
                executor_message = ex.Message
            });
            var unknown = actions.MarkOutcomeUnknown(
                request.ActionId,
                uncertaintyEvidence,
                JsonSerializer.Serialize(new
                {
                    executor_exception = ex.GetType().FullName,
                    executor_message = ex.Message
                }));

            return new ConsequentialActionResult(
                ActionReservationDisposition.Execute,
                unknown,
                true,
                inspection?.Satisfied,
                uncertaintyEvidence,
                ex.Message);
        }
    }
}
