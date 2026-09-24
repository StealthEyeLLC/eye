using System.Security.Cryptography;
using System.Text.Json;

namespace StealthEye.Runtime;

public interface IPostconditionInspector
{
    string Kind { get; }
    ValueTask<PostconditionInspection> InspectAsync(string specJson, CancellationToken cancellationToken = default);
}

public sealed class PostconditionInspectorRegistry(IEnumerable<IPostconditionInspector> inspectors)
{
    private readonly IReadOnlyDictionary<string, IPostconditionInspector> _inspectors =
        inspectors.ToDictionary(x => x.Kind, StringComparer.Ordinal);

    public bool TryGet(string kind, out IPostconditionInspector inspector) =>
        _inspectors.TryGetValue(kind, out inspector!);
}

public sealed class FilePostconditionInspector : IPostconditionInspector
{
    public const string InspectorKind = "file";

    public string Kind => InspectorKind;

    public async ValueTask<PostconditionInspection> InspectAsync(
        string specJson,
        CancellationToken cancellationToken = default)
    {
        FilePostconditionSpec spec;
        try
        {
            spec = JsonSerializer.Deserialize<FilePostconditionSpec>(specJson)
                ?? throw new JsonException("File postcondition spec was null.");
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Invalid file postcondition spec JSON.", nameof(specJson), ex);
        }

        if (string.IsNullOrWhiteSpace(spec.Path))
            throw new ArgumentException("File postcondition requires path.", nameof(specJson));
        if (spec.MinBytes < 0)
            throw new ArgumentException("min_bytes cannot be negative.", nameof(specJson));
        if (spec.Sha256 is not null &&
            (spec.Sha256.Length != 64 || spec.Sha256.Any(ch => !Uri.IsHexDigit(ch))))
            throw new ArgumentException("sha256 must be a 64-character hex digest.", nameof(specJson));

        var fullPath = Path.GetFullPath(spec.Path);
        if (!File.Exists(fullPath))
        {
            var missing = JsonSerializer.Serialize(new
            {
                kind = InspectorKind,
                path = fullPath,
                exists = false,
                min_bytes = spec.MinBytes,
                expected_sha256 = spec.Sha256
            });
            return new PostconditionInspection(false, missing, "file_missing", "Expected file does not exist.");
        }

        var info = new FileInfo(fullPath);
        string sha256;
        await using (var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            131072,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            sha256 = Convert.ToHexString(hash).ToLowerInvariant();
        }

        var sizeSatisfied = info.Length >= spec.MinBytes;
        var hashSatisfied = spec.Sha256 is null ||
            string.Equals(spec.Sha256, sha256, StringComparison.OrdinalIgnoreCase);
        var satisfied = sizeSatisfied && hashSatisfied;

        var evidence = JsonSerializer.Serialize(new
        {
            kind = InspectorKind,
            path = fullPath,
            exists = true,
            size_bytes = info.Length,
            min_bytes = spec.MinBytes,
            sha256,
            expected_sha256 = spec.Sha256,
            size_satisfied = sizeSatisfied,
            hash_satisfied = hashSatisfied
        });

        return satisfied
            ? new PostconditionInspection(true, evidence)
            : new PostconditionInspection(
                false,
                evidence,
                sizeSatisfied ? "hash_mismatch" : "file_too_small",
                sizeSatisfied ? "File SHA-256 did not match the expected value." : "File was smaller than required.");
    }

    private sealed record FilePostconditionSpec(
        [property: System.Text.Json.Serialization.JsonPropertyName("path")] string Path,
        [property: System.Text.Json.Serialization.JsonPropertyName("min_bytes")] long MinBytes = 1,
        [property: System.Text.Json.Serialization.JsonPropertyName("sha256")] string? Sha256 = null);
}

public sealed class ActionReconciler(
    ActionJournalStore actions,
    PostconditionInspectorRegistry inspectors)
{
    public async ValueTask<ActionReconciliationResult> InspectUnknownAsync(
        string actionId,
        CancellationToken cancellationToken = default)
    {
        var action = actions.GetRequired(actionId);
        if (action.State != ActionStates.OutcomeUnknown)
            throw new InvalidOperationException(
                $"Action '{actionId}' must be '{ActionStates.OutcomeUnknown}' before reconciliation.");

        if (action.Postcondition is null)
        {
            return new ActionReconciliationResult(
                actionId,
                action.State,
                null,
                null,
                true,
                "No deterministic postcondition is registered. The action remains outcome_unknown and must not be replayed automatically.");
        }

        if (!inspectors.TryGet(action.Postcondition.Kind, out var inspector))
        {
            return new ActionReconciliationResult(
                actionId,
                action.State,
                null,
                null,
                true,
                $"No postcondition inspector is registered for kind '{action.Postcondition.Kind}'. The action remains outcome_unknown.");
        }

        var inspection = await inspector.InspectAsync(action.Postcondition.SpecJson, cancellationToken);
        if (inspection.Satisfied)
        {
            var verified = actions.MarkVerified(
                actionId,
                inspection.EvidenceJson,
                JsonSerializer.Serialize(new { reconciled_after_restart = true }));
            return new ActionReconciliationResult(
                actionId,
                verified.State,
                true,
                inspection.EvidenceJson,
                false,
                "Postcondition passed. The prior action is verified and was not replayed.");
        }

        return new ActionReconciliationResult(
            actionId,
            action.State,
            false,
            inspection.EvidenceJson,
            true,
            "Postcondition did not pass. The action remains outcome_unknown; replay requires an explicit retry decision.");
    }
}

