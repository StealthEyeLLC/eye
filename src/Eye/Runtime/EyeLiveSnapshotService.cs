using System.Security.Principal;
using StealthEye.Contract;

namespace StealthEye.Runtime;

public sealed class EyeLiveSnapshotService(
    JobStore jobStore,
    TriggerStore triggerStore,
    ArtifactStore artifactStore,
    EngineSupervisor engineSupervisor)
{
    private const int RecentLimit = 20;

    public EyeLiveSnapshotResult Snapshot()
    {
        var engine = engineSupervisor.Status();
        return new EyeLiveSnapshotResult(
            DateTimeOffset.UtcNow,
            new EyeLiveMachineResult(Environment.MachineName, WindowsIdentity.GetCurrent().Name),
            new EyeLiveEngineResult(
                engine.State,
                engine.ActiveVersion,
                engine.PreviousVersion,
                engine.EngineVersion,
                engine.ProcessId,
                engine.LastError),
            jobStore.ListRecent(RecentLimit).Select(job => new EyeLiveJobResult(
                job.JobId,
                job.Incarnation,
                job.State,
                job.Context,
                job.Terminal,
                job.Pid,
                job.CreatedAt,
                job.CompletedAt,
                job.FailureMessage)).ToArray(),
            triggerStore.ListRecent(RecentLimit).Select(trigger => new EyeLiveTriggerResult(
                trigger.TriggerId,
                trigger.Incarnation,
                trigger.Kind,
                trigger.State,
                trigger.CreatedAt,
                trigger.DeadlineAt,
                trigger.ProcessId,
                trigger.FilePath,
                trigger.FailureMessage)).ToArray(),
            artifactStore.ListRecent(RecentLimit).Select(artifact => new EyeLiveArtifactResult(
                artifact.ArtifactId,
                artifact.Incarnation,
                artifact.Kind,
                artifact.Name,
                artifact.SizeBytes,
                artifact.StorageTier,
                artifact.CreatedAt)).ToArray());
    }
}