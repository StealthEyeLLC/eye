using System.Text.Json.Serialization;

namespace StealthEye.Runtime;

public static class ActionStates
{
    public const string Ready = "ready";
    public const string Reserved = "reserved";
    public const string Dispatching = "dispatching";
    public const string Running = "running";
    public const string OutcomeUnknown = "outcome_unknown";
    public const string Verified = "verified";
    public const string Failed = "failed";

    public static bool IsTerminal(string state) => state is Verified or Failed;
}

public enum ActionReservationDisposition
{
    Execute,
    InProgress,
    ReturnPrior,
    InspectBeforeReplay
}

public sealed record ActionPostconditionContract(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("spec_json")] string SpecJson);

public sealed record ActionRecord(
    [property: JsonPropertyName("action_id")] string ActionId,
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("capability")] string Capability,
    [property: JsonPropertyName("input_sha256")] string InputSha256,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("postcondition")] ActionPostconditionContract? Postcondition,
    [property: JsonPropertyName("evidence_json")] string? EvidenceJson,
    [property: JsonPropertyName("result_json")] string? ResultJson,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("verified_at")] DateTimeOffset? VerifiedAt);

public sealed record ActionReservation(
    [property: JsonPropertyName("disposition")] ActionReservationDisposition Disposition,
    [property: JsonPropertyName("action")] ActionRecord Action);

public sealed record PostconditionInspection(
    [property: JsonPropertyName("satisfied")] bool Satisfied,
    [property: JsonPropertyName("evidence_json")] string EvidenceJson,
    [property: JsonPropertyName("failure_code")] string? FailureCode = null,
    [property: JsonPropertyName("failure_message")] string? FailureMessage = null);

public sealed record ActionReconciliationResult(
    [property: JsonPropertyName("action_id")] string ActionId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("postcondition_satisfied")] bool? PostconditionSatisfied,
    [property: JsonPropertyName("evidence_json")] string? EvidenceJson,
    [property: JsonPropertyName("requires_manual_decision")] bool RequiresManualDecision,
    [property: JsonPropertyName("message")] string Message);
