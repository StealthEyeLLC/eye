using System.Security.Principal;
using System.Text.Json;
using StealthEye.Contract;

namespace StealthEye.Runtime;

public enum EyeEffectClass
{
    Inspect,
    Run,
    Change,
    Interact,
    External
}

public sealed class EyeDispatcher(JobManager jobManager, ArtifactStore artifactStore, EngineSupervisor? engineSupervisor = null, DesktopObservationService? desktopObservationService = null, UiaQueryService? uiaQueryService = null, UiaActionService? uiaActionService = null, BrowserObservationService? browserObservationService = null, BrowserControlService? browserControlService = null, ActionJournalStore? actionJournalStore = null, ConsequentialActionRunner? consequentialActionRunner = null, ActionReconciler? actionReconciler = null, EyeContractCatalog? publicContract = null)
{
    private const int FastCompletionWindowMs = 1000;
    private const long InlineOutputLimitBytes = 262_144;

    public async Task<object> ExecuteAsync(
        EyeEffectClass effectClass,
        string op,
        JsonElement? args,
        ActionExecutionEnvelope? actionEnvelope,
        CancellationToken cancellationToken = default)
    {
        if (actionEnvelope is null || actionEnvelope.IsEmpty)
            return await ExecuteAsync(effectClass, op, args, cancellationToken);

        try
        {
            var requiredClass = GetEffectClass(op);
            if (requiredClass is null || requiredClass.Value != effectClass)
                return await ExecuteAsync(effectClass, op, args, cancellationToken);

            ValidateActionEnvelope(actionEnvelope);
            var inputSha256 = ActionInputHasher.Compute(effectClass, op, args, actionEnvelope);
            var request = new ConsequentialActionRequest(
                actionEnvelope.TaskId!,
                actionEnvelope.ActionId!,
                $"{GetFacadeName(effectClass)}:{op}",
                inputSha256,
                actionEnvelope.Postcondition!);

            var outcome = await RequireConsequentialActionRunner().ExecuteAsync(
                request,
                async ct => (object?)await ExecuteAsync(effectClass, op, args, ct),
                cancellationToken);

            if (outcome.Disposition == ActionReservationDisposition.InspectBeforeReplay)
            {
                await RequireActionReconciler().InspectUnknownAsync(
                    actionEnvelope.ActionId!,
                    cancellationToken);
                var reconciled = RequireActionJournalStore().GetRequired(actionEnvelope.ActionId!);
                return MaterializeActionResult(op, reconciled);
            }

            return MaterializeActionResult(op, outcome.Action);
        }
        catch (ArgumentException ex)
        {
            return Failure(op, "invalid_argument", ex.Message, true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(op, "request_cancelled", "The Eye request was cancelled.", true);
        }
        catch (Exception ex)
        {
            return Failure(op, "operation_failed", ex.Message, false);
        }
    }
    public async Task<object> ExecuteAsync(
        EyeEffectClass effectClass,
        string op,
        JsonElement? args,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var requiredClass = GetEffectClass(op);
            if (requiredClass is not null && requiredClass.Value != effectClass)
            {
                return Failure(
                    op,
                    "wrong_tool",
                    $"Operation '{op}' belongs to {GetFacadeName(requiredClass.Value)}, not {GetFacadeName(effectClass)}.",
                    true,
                    new { tool = GetFacadeName(requiredClass.Value) });
            }

            switch (op)
            {
                case "engine.status":
                    return Success(op, ToPublic(RequireEngineSupervisor().Status()));
                case "system.status":
                    return Success(op, new SystemStatusResult(
                        "StealthEye",
                        "eye",
                        typeof(EyeDispatcher).Assembly.GetName().Version?.ToString() ?? "unknown",
                        "eye-mcp-v2",
                        Environment.ProcessId,
                        Environment.MachineName,
                        WindowsIdentity.GetCurrent().Name,
                        System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()));

                case "action.status":
                {
                    var request = DeserializeRequired<ActionIdArgs>(op, args);
                    return Success(op, ToPublic(RequireActionJournalStore().GetRequired(request.ActionId)));
                }
                case "capabilities":
                {
                    var contract = RequirePublicContract();
                    return Success(op, new CapabilitiesResult(
                        "eye-mcp-v2",
                        new CapabilityFacades(
                            OperationsFor(contract, "eye_inspect"),
                            OperationsFor(contract, "eye_run"),
                            OperationsFor(contract, "eye_change"),
                            OperationsFor(contract, "eye_interact"),
                            OperationsFor(contract, "eye_external"),
                            OperationsFor(contract, "eye_live"))));
                }

                case "browser.observe":
                {
                    var snapshot = await RequireBrowserObservationService().ObserveAsync(cancellationToken);
                    return Success(op, new BrowserObserveResult(
                        snapshot.Cursor,
                        snapshot.ObservedAt,
                        snapshot.BrowserVersion,
                        snapshot.ProtocolVersion,
                        snapshot.Targets.Select(target => new BrowserTargetResult(
                            target.TargetId,
                            target.Incarnation,
                            target.Type,
                            target.Title,
                            target.Url)).ToArray()));
                }
                case "ui.observe":
                {
                    var request = args is null || args.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                        ? new UiObserveArgs()
                        : args.Value.Deserialize<UiObserveArgs>() ?? new UiObserveArgs();
                    var snapshot = await RequireDesktopObservationService().ObserveAsync(request.IncludeInvisible, cancellationToken);
                    return Success(op, new UiObserveResult(
                        snapshot.Cursor,
                        snapshot.SessionId,
                        snapshot.ObservedAt,
                        snapshot.Windows.Select(window => new UiWindowResult(
                            window.WindowId,
                            window.Incarnation,
                            window.ProcessId,
                            window.ProcessName,
                            window.Title,
                            window.ClassName,
                            window.Visible,
                            window.Minimized,
                            window.Foreground,
                            new UiWindowBoundsResult(window.Left, window.Top, window.Right, window.Bottom),
                            window.Uia is null ? null : new UiUiaRootResult(
                                window.Uia.Name,
                                window.Uia.AutomationId,
                                window.Uia.ControlType,
                                window.Uia.FrameworkId,
                                window.Uia.ClassName,
                                window.Uia.Enabled,
                                window.Uia.Offscreen))).ToArray()));
                }
                case "ui.query":
                {
                    var request = DeserializeRequired<UiQueryArgs>(op, args);
                    var snapshot = await RequireUiaQueryService().QueryAsync(
                        request.WindowId,
                        request.MaxDepth,
                        request.MaxNodes,
                        cancellationToken);
                    return Success(op, new UiQueryResult(
                        snapshot.Cursor,
                        snapshot.WindowId,
                        snapshot.WindowIncarnation,
                        snapshot.ObservedAt,
                        snapshot.Truncated,
                        snapshot.Elements.Select(element => new UiElementResult(
                            element.ElementId,
                            element.Incarnation,
                            element.ParentElementId,
                            element.Depth,
                            element.Name,
                            element.AutomationId,
                            element.ControlType,
                            element.FrameworkId,
                            element.ClassName,
                            element.Enabled,
                            element.Offscreen,
                            element.Focused,
                            new UiWindowBoundsResult(element.Left, element.Top, element.Right, element.Bottom))).ToArray()));
                }
                case "run":
                {
                    var request = DeserializeRequired<RunRequest>(op, args);
                    ValidateRunRequest(request);
                    var job = jobManager.Start(request);
                    var waited = await jobManager.WaitAsync(job.JobId, FastCompletionWindowMs, cancellationToken);
                    if (!waited.WaitTimedOut)
                    {
                        var inline = await jobManager.TryGetInlineProcessResultAsync(
                            waited.Job,
                            InlineOutputLimitBytes,
                            cancellationToken);
                        if (inline is not null)
                        {
                            return Success(op, new RunOperationResult(
                                inline.Pid,
                                inline.ExitCode,
                                inline.TimedOut,
                                inline.Stdout,
                                inline.Stderr,
                                inline.Context,
                                inline.EffectiveIdentity,
                                inline.DurationMs));
                        }
                    }

                    var current = waited.Job;
                    return Success(op, new JobReferenceResult(current.JobId, current.Incarnation, current.State));
                }

                case "job.start":
                {
                    var request = DeserializeRequired<JobStartArgs>(op, args);
                    ValidateJobStartRequest(request);
                    var runRequest = ToRunRequest(request);
                    ValidateRunRequest(runRequest);
                    var job = jobManager.Start(runRequest, request.Terminal, request.Columns, request.Rows);
                    return Success(op, new JobReferenceResult(job.JobId, job.Incarnation, job.State));
                }

                case "job.write":
                {
                    var request = DeserializeRequired<JobWriteArgs>(op, args);
                    if (request.Text is null || request.Text.Length > 262_144)
                        throw new ArgumentException("text must contain at most 262144 characters.");
                    var written = await jobManager.WriteAsync(request.JobId, request.Text, cancellationToken);
                    var current = jobManager.Status(request.JobId);
                    return Success(op, new JobWriteResult(current.JobId, written, current.State));
                }

                case "job.resize":
                {
                    var request = DeserializeRequired<JobResizeArgs>(op, args);
                    var resized = jobManager.Resize(request.JobId, request.Columns, request.Rows);
                    return Success(op, new JobResizeResult(resized.JobId, resized.Columns!.Value, resized.Rows!.Value, resized.State));
                }

                case "job.status":
                {
                    var request = DeserializeRequired<JobIdArgs>(op, args);
                    return Success(op, ToPublic(jobManager.Status(request.JobId)));
                }

                case "job.read":
                {
                    var request = DeserializeRequired<JobReadArgs>(op, args);
                    var read = await jobManager.ReadAsync(
                        request.JobId,
                        request.Stream,
                        request.Cursor,
                        request.MaxBytes,
                        cancellationToken);
                    return Success(op, new JobReadPublicResult(
                        read.JobId,
                        read.Stream,
                        read.Cursor,
                        read.Text,
                        read.NextCursor,
                        read.Eof,
                        read.State));
                }

                case "job.wait":
                {
                    var request = DeserializeRequired<JobWaitArgs>(op, args);
                    var waited = await jobManager.WaitAsync(request.JobId, request.WaitMs, cancellationToken);
                    return Success(op, new JobWaitPublicResult(ToPublic(waited.Job), waited.WaitTimedOut));
                }

                case "job.cancel":
                {
                    var request = DeserializeRequired<JobIdArgs>(op, args);
                    var cancelled = await jobManager.CancelAsync(request.JobId, cancellationToken);
                    return Success(op, new JobCancelResult(cancelled.JobId, cancelled.State));
                }

                case "job.result":
                {
                    var request = DeserializeRequired<JobIdArgs>(op, args);
                    return Success(op, ToPublic(jobManager.Result(request.JobId)));
                }

                case "job.attach":
                {
                    var request = DeserializeRequired<JobIdArgs>(op, args);
                    var attached = jobManager.Attach(request.JobId);
                    return Success(op, new JobAttachResult(ToPublic(attached.Job), attached.StdoutCursor, attached.StderrCursor));
                }

                case "browser.navigate":
                {
                    var request = DeserializeRequired<BrowserNavigateArgs>(op, args);
                    var result = await RequireBrowserControlService().NavigateAsync(
                        request.TargetId,
                        request.Url,
                        cancellationToken);
                    return Success(op, new BrowserNavigateResult(
                        result.TargetId,
                        result.Incarnation,
                        result.Url,
                        result.FrameId,
                        result.LoaderId,
                        result.ErrorText));
                }

                case "browser.evaluate":
                {
                    var request = DeserializeRequired<BrowserEvaluateArgs>(op, args);
                    var result = await RequireBrowserControlService().EvaluateAsync(
                        request.TargetId,
                        request.Expression,
                        cancellationToken);
                    return Success(op, new BrowserEvaluateResult(
                        result.TargetId,
                        result.Incarnation,
                        result.Type,
                        result.Value,
                        result.Description,
                        result.Threw,
                        result.ExceptionText));
                }
                case "ui.act":
                {
                    var request = DeserializeRequired<UiActArgs>(op, args);
                    var result = await RequireUiaActionService().ActAsync(
                        request.ElementId,
                        request.Action,
                        request.Value,
                        cancellationToken);
                    return Success(op, new UiActResult(
                        result.ElementId,
                        result.Incarnation,
                        result.Action,
                        result.Completed,
                        result.CompletedAt));
                }
                case "artifact.info":
                {
                    var request = DeserializeRequired<ArtifactIdArgs>(op, args);
                    return Success(op, ToPublic(artifactStore.Info(request.ArtifactId)));
                }

                case "artifact.preview":
                {
                    var request = DeserializeRequired<ArtifactPreviewArgs>(op, args);
                    var preview = await artifactStore.PreviewAsync(request.ArtifactId, request.MaxChars, cancellationToken);
                    return Success(op, new ArtifactPreviewPublicResult(
                        preview.ArtifactId,
                        preview.TextAvailable,
                        preview.Text,
                        preview.Truncated));
                }

                case "artifact.read_range":
                {
                    var request = DeserializeRequired<ArtifactReadRangeArgs>(op, args);
                    var range = await artifactStore.ReadRangeAsync(
                        request.ArtifactId,
                        request.Offset,
                        request.MaxBytes,
                        cancellationToken);
                    return Success(op, new ArtifactReadRangeResult(
                        range.ArtifactId,
                        range.Offset,
                        range.BytesRead,
                        range.NextOffset,
                        range.Eof,
                        range.DataBase64));
                }

                case "artifact.diff":
                {
                    var request = DeserializeRequired<ArtifactDiffArgs>(op, args);
                    var diff = await artifactStore.DiffAsync(
                        request.LeftArtifactId,
                        request.RightArtifactId,
                        cancellationToken);
                    return Success(op, new ArtifactDiffPublicResult(
                        diff.LeftArtifactId,
                        diff.RightArtifactId,
                        diff.Equal,
                        diff.LeftSizeBytes,
                        diff.RightSizeBytes,
                        diff.LeftSha256,
                        diff.RightSha256,
                        diff.FirstDifferenceOffset));
                }

                case "engine.activate":
                {
                    var request = DeserializeRequired<EngineActivateArgs>(op, args);
                    return Success(op, ToPublic(await RequireEngineSupervisor().ActivateAsync(request.Version, cancellationToken)));
                }

                case "engine.restart":
                    return Success(op, ToPublic(await RequireEngineSupervisor().RestartAsync(cancellationToken)));

                case "engine.rollback":
                    return Success(op, ToPublic(await RequireEngineSupervisor().RollbackAsync(cancellationToken)));
                case "artifact.export":
                {
                    var request = DeserializeRequired<ArtifactExportArgs>(op, args);
                    var artifact = await artifactStore.ExportAsync(
                        request.ArtifactId,
                        request.Destination,
                        request.Overwrite,
                        cancellationToken);
                    return Success(op, new ArtifactExportResult(
                        artifact.ArtifactId,
                        Path.GetFullPath(request.Destination),
                        artifact.SizeBytes,
                        artifact.Sha256));
                }

                case "artifact.delete":
                {
                    var request = DeserializeRequired<ArtifactIdArgs>(op, args);
                    return Success(op, new ArtifactDeleteResult(request.ArtifactId, artifactStore.Delete(request.ArtifactId)));
                }
                default:
                    return Failure(
                        op,
                        "unknown_operation",
                        $"Unknown Eye operation for {GetFacadeName(effectClass)}: {op}",
                        false);
            }
        }
        catch (ArgumentException ex)
        {
            return Failure(op, "invalid_argument", ex.Message, true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(op, "request_cancelled", "The Eye request was cancelled.", true);
        }
        catch (Exception ex)
        {
            return Failure(op, "operation_failed", ex.Message, false);
        }
    }


    private static void ValidateActionEnvelope(ActionExecutionEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.TaskId) ||
            string.IsNullOrWhiteSpace(envelope.ActionId) ||
            envelope.Postcondition is null)
            throw new ArgumentException(
                "task_id, action_id, and postcondition must be supplied together for idempotent execution.");

        if (envelope.TaskId.Length > 256)
            throw new ArgumentException("task_id must contain at most 256 characters.");
        if (envelope.ActionId.Length > 256)
            throw new ArgumentException("action_id must contain at most 256 characters.");
        if (envelope.Postcondition.Kind is not (FilePostconditionInspector.InspectorKind or CommandPostconditionInspector.InspectorKind))
            throw new ArgumentException("postcondition.kind must be file or command.");

        using var spec = JsonDocument.Parse(envelope.Postcondition.SpecJson);
        if (spec.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("postcondition.spec must be a JSON object.");
    }

    private object MaterializeActionResult(string op, ActionRecord action)
    {
        if (action.State == ActionStates.Verified)
        {
            if (TryReadEyeResult(action.ResultJson, out var prior))
                return prior;

            return Failure(
                op,
                "action_verified_result_unavailable",
                "The action is verified and was not replayed, but its original operation result is unavailable.",
                false,
                ToPublic(action));
        }

        if (action.State == ActionStates.Failed)
        {
            if (TryReadEyeResult(action.ResultJson, out var prior) &&
                prior.TryGetProperty("ok", out var ok) &&
                ok.ValueKind == JsonValueKind.False)
                return prior;

            return Failure(
                op,
                "postcondition_failed",
                "The operation ran but its deterministic postcondition did not pass.",
                false,
                ToPublic(action));
        }

        if (action.State == ActionStates.OutcomeUnknown)
        {
            return Failure(
                op,
                "action_outcome_unknown",
                "The prior action outcome remains unknown and will not be replayed automatically.",
                false,
                ToPublic(action));
        }

        return Failure(
            op,
            "action_in_progress",
            "The same action_id is already reserved or running.",
            true,
            ToPublic(action));
    }

    private static bool TryReadEyeResult(string? json, out JsonElement result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("ok", out var ok) ||
                ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return false;

            result = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
    private static void ValidateJobStartRequest(JobStartArgs request)
    {
        if (request.Columns is < 1 or > short.MaxValue)
            throw new ArgumentException($"columns must be between 1 and {short.MaxValue}.");
        if (request.Rows is < 1 or > short.MaxValue)
            throw new ArgumentException($"rows must be between 1 and {short.MaxValue}.");
    }
    private static RunRequest ToRunRequest(JobStartArgs request) => new()
    {
        Context = request.Context,
        FileName = request.FileName,
        Arguments = request.Arguments ?? [],
        WorkingDirectory = request.WorkingDirectory,
        TimeoutMs = request.TimeoutMs
    };
    private static void ValidateRunRequest(RunRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FileName))
            throw new ArgumentException("file_name is required.");
        if (request.Context is not ("system" or "user" or "wsl"))
            throw new ArgumentException("context must be system, user, or wsl.");
        if (request.TimeoutMs is < 1 or > 86_400_000)
            throw new ArgumentException("timeout_ms must be between 1 and 86400000.");
    }
    private static T DeserializeRequired<T>(string op, JsonElement? args)
    {
        if (args is null || args.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            throw new ArgumentException($"{op} requires args.");
        return args.Value.Deserialize<T>()
            ?? throw new ArgumentException($"Unable to deserialize {op} args.");
    }

    private static ActionStatusResult ToPublic(ActionRecord action) => new(
        action.ActionId,
        action.TaskId,
        action.Capability,
        action.InputSha256,
        action.State,
        action.Postcondition?.Kind,
        action.EvidenceJson,
        action.ResultJson,
        action.CreatedAt,
        action.UpdatedAt,
        action.VerifiedAt);
    private static JobStatusResult ToPublic(JobRecord job) => new(
        job.JobId,
        job.Incarnation,
        job.State,
        job.Context,
        job.Terminal,
        job.Columns,
        job.Rows,
        job.Pid,
        job.EffectiveIdentity,
        job.CreatedAt,
        job.StartedAt,
        job.CompletedAt,
        job.ExitCode,
        job.TimedOut,
        job.FailureCode,
        job.FailureMessage);

    private static EngineStatusResult ToPublic(EngineSupervisorStatus status) => new(
        status.State,
        status.ActiveVersion,
        status.PreviousVersion,
        status.EngineVersion,
        status.ProcessId,
        status.LastError);

    private EyeContractCatalog RequirePublicContract() =>
        publicContract ?? EyeContractCatalog.Load();

    private static string[] OperationsFor(EyeContractCatalog contract, string toolName) =>
        contract.Descriptors
            .Single(x => string.Equals(x.Name, toolName, StringComparison.Ordinal))
            .Operations
            .Select(x => x.Id)
            .ToArray();
    private ActionJournalStore RequireActionJournalStore() =>
        actionJournalStore ?? throw new InvalidOperationException("Action journal is not configured.");

    private ConsequentialActionRunner RequireConsequentialActionRunner() =>
        consequentialActionRunner ?? throw new InvalidOperationException("Consequential action runner is not configured.");

    private ActionReconciler RequireActionReconciler() =>
        actionReconciler ?? throw new InvalidOperationException("Action reconciler is not configured.");
    private BrowserObservationService RequireBrowserObservationService() =>
        browserObservationService ?? throw new InvalidOperationException("Browser observation service is not configured.");

    private BrowserControlService RequireBrowserControlService() =>
        browserControlService ?? throw new InvalidOperationException("Browser control service is not configured.");

    private UiaActionService RequireUiaActionService() =>
        uiaActionService ?? throw new InvalidOperationException("UIA action service is not configured.");
    private UiaQueryService RequireUiaQueryService() =>
        uiaQueryService ?? throw new InvalidOperationException("UIA query service is not configured.");
    private DesktopObservationService RequireDesktopObservationService() =>
        desktopObservationService ?? throw new InvalidOperationException("Desktop observation service is not configured.");
    private EngineSupervisor RequireEngineSupervisor() =>
        engineSupervisor ?? throw new InvalidOperationException("Engine supervisor is not configured.");
    private static ArtifactInfoResult ToPublic(ArtifactRecord artifact) => new(
        artifact.ArtifactId,
        artifact.Incarnation,
        artifact.Kind,
        artifact.MimeType,
        artifact.SizeBytes,
        artifact.Sha256,
        artifact.Name,
        artifact.StorageTier,
        artifact.Provenance,
        artifact.CreatedAt);
    private static EyeSuccess<T> Success<T>(string op, T result) => new(true, op, result);

    private static EyeFailure Failure(
        string op,
        string code,
        string message,
        bool retryable,
        object? expected = null) =>
        new(false, op, new EyeError(code, message, retryable, expected));

    private EyeEffectClass? GetEffectClass(string op)
    {
        var tool = RequirePublicContract().Descriptors.FirstOrDefault(
            x => x.Operations.Any(operation =>
                string.Equals(operation.Id, op, StringComparison.Ordinal)));
        return tool is null ? null : ParseEffectClass(tool.EffectClass);
    }

    private string GetFacadeName(EyeEffectClass effectClass)
    {
        var contractEffectClass = ToContractEffectClass(effectClass);
        return RequirePublicContract().Descriptors
            .Single(x => string.Equals(
                x.EffectClass,
                contractEffectClass,
                StringComparison.Ordinal))
            .Name;
    }

    private static EyeEffectClass ParseEffectClass(string effectClass) => effectClass switch
    {
        "inspect" => EyeEffectClass.Inspect,
        "run" => EyeEffectClass.Run,
        "change" => EyeEffectClass.Change,
        "interact" => EyeEffectClass.Interact,
        "external" => EyeEffectClass.External,
        _ => throw new InvalidOperationException(
            $"Unsupported contract effect class '{effectClass}'.")
    };

    private static string ToContractEffectClass(EyeEffectClass effectClass) => effectClass switch
    {
        EyeEffectClass.Inspect => "inspect",
        EyeEffectClass.Run => "run",
        EyeEffectClass.Change => "change",
        EyeEffectClass.Interact => "interact",
        EyeEffectClass.External => "external",
        _ => throw new ArgumentOutOfRangeException(nameof(effectClass), effectClass, null)
    };
}
