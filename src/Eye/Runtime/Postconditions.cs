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


public sealed class CommandPostconditionInspector(ProcessRunner runner) : IPostconditionInspector
{
    public const string InspectorKind = "command";
    private const int MaxTimeoutMs = 30_000;
    private const int EvidenceExcerptLimit = 4096;

    public string Kind => InspectorKind;

    public async ValueTask<PostconditionInspection> InspectAsync(
        string specJson,
        CancellationToken cancellationToken = default)
    {
        CommandPostconditionSpec spec;
        try
        {
            spec = JsonSerializer.Deserialize<CommandPostconditionSpec>(specJson)
                ?? throw new JsonException("Command postcondition spec was null.");
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Invalid command postcondition spec JSON.", nameof(specJson), ex);
        }

        if (string.IsNullOrWhiteSpace(spec.FileName))
            throw new ArgumentException("Command postcondition requires file_name.", nameof(specJson));
        if (spec.Context is not ("system" or "user" or "wsl"))
            throw new ArgumentException("Command postcondition context must be system, user, or wsl.", nameof(specJson));
        if (spec.TimeoutMs is < 1 or > MaxTimeoutMs)
            throw new ArgumentException($"Command postcondition timeout_ms must be between 1 and {MaxTimeoutMs}.", nameof(specJson));

        var result = await runner.RunAsync(
            new RunRequest
            {
                Context = spec.Context,
                FileName = spec.FileName,
                Arguments = spec.Arguments ?? [],
                WorkingDirectory = spec.WorkingDirectory,
                TimeoutMs = spec.TimeoutMs
            },
            cancellationToken);

        var exitSatisfied = !result.TimedOut && result.ExitCode == spec.ExpectedExitCode;
        var stdoutSatisfied = spec.StdoutContains is null ||
            result.Stdout.Contains(spec.StdoutContains, StringComparison.Ordinal);
        var stderrSatisfied = spec.StderrContains is null ||
            result.Stderr.Contains(spec.StderrContains, StringComparison.Ordinal);
        var satisfied = exitSatisfied && stdoutSatisfied && stderrSatisfied;

        var evidence = JsonSerializer.Serialize(new
        {
            kind = InspectorKind,
            context = result.Context,
            effective_identity = result.EffectiveIdentity,
            pid = result.Pid,
            exit_code = result.ExitCode,
            expected_exit_code = spec.ExpectedExitCode,
            timed_out = result.TimedOut,
            stdout_contains = spec.StdoutContains,
            stderr_contains = spec.StderrContains,
            stdout_satisfied = stdoutSatisfied,
            stderr_satisfied = stderrSatisfied,
            stdout_excerpt = Excerpt(result.Stdout),
            stderr_excerpt = Excerpt(result.Stderr)
        });

        if (satisfied)
            return new PostconditionInspection(true, evidence);

        var code = result.TimedOut
            ? "verification_timed_out"
            : !exitSatisfied
                ? "verification_exit_mismatch"
                : !stdoutSatisfied
                    ? "verification_stdout_mismatch"
                    : "verification_stderr_mismatch";

        return new PostconditionInspection(
            false,
            evidence,
            code,
            "Command postcondition was not satisfied.");
    }

    private static string Excerpt(string value) =>
        value.Length <= EvidenceExcerptLimit ? value : value[..EvidenceExcerptLimit];

    private sealed record CommandPostconditionSpec(
        [property: System.Text.Json.Serialization.JsonPropertyName("file_name")] string FileName,
        [property: System.Text.Json.Serialization.JsonPropertyName("context")] string Context = "system",
        [property: System.Text.Json.Serialization.JsonPropertyName("arguments")] string[]? Arguments = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("working_directory")] string? WorkingDirectory = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("timeout_ms")] int TimeoutMs = 5000,
        [property: System.Text.Json.Serialization.JsonPropertyName("expected_exit_code")] int ExpectedExitCode = 0,
        [property: System.Text.Json.Serialization.JsonPropertyName("stdout_contains")] string? StdoutContains = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("stderr_contains")] string? StderrContains = null);
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
