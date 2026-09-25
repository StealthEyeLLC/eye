using System.Security.Principal;
using System.Text;
using StealthEye.Contract;

namespace StealthEye.Runtime;

public sealed class EyeLiveSnapshotService(
    JobStore jobStore,
    TriggerStore triggerStore,
    ArtifactStore artifactStore,
    MissionBlackboardStore blackboard,
    EngineSupervisor engineSupervisor)
{
    private const int RecentLimit = 20;
    private const int MissionLimit = 10;
    private const int TailBytes = 2048;

    public EyeLiveSnapshotResult Snapshot()
    {
        var engine = engineSupervisor.Status();
        var jobs = jobStore.ListRecent(RecentLimit);
        var triggers = triggerStore.ListRecent(RecentLimit);
        var artifacts = artifactStore.ListRecent(RecentLimit);
        var missions = blackboard.ListRecent(MissionLimit);
        var relay = missions
            .SelectMany(mission => mission.Relay.Select(entry => new EyeLiveRelayResult(
                mission.MissionId,
                entry.Cursor,
                entry.Source,
                entry.Message,
                entry.CreatedAt)))
            .OrderByDescending(entry => entry.CreatedAt)
            .Take(RecentLimit)
            .ToArray();

        var activeJobCount = jobs.Count(job => !JobStates.IsTerminal(job.State));
        var pendingTriggerCount = triggers.Count(trigger => trigger.State == TriggerStates.Pending);
        var relayMessageCount = missions.Sum(mission => mission.Relay.Length);

        return new EyeLiveSnapshotResult(
            DateTimeOffset.UtcNow,
            new EyeLiveMachineResult(Environment.MachineName, WindowsIdentity.GetCurrent().Name),
            new EyeLiveContextResult(
                missions.Length,
                activeJobCount,
                pendingTriggerCount,
                relayMessageCount,
                missions.FirstOrDefault()?.MissionId),
            new EyeLiveEngineResult(
                engine.State,
                engine.ActiveVersion,
                engine.PreviousVersion,
                engine.EngineVersion,
                engine.ProcessId,
                engine.LastError),
            missions.Select(mission => new EyeLiveMissionResult(
                mission.MissionId,
                mission.Incarnation,
                mission.Revision,
                mission.Objective,
                mission.NextAction,
                mission.Relay.Length,
                mission.UpdatedAt)).ToArray(),
            relay,
            jobs.Select(job => new EyeLiveJobResult(
                job.JobId,
                job.Incarnation,
                job.State,
                job.Context,
                job.Terminal,
                job.Pid,
                job.CreatedAt,
                job.CompletedAt,
                job.FailureMessage,
                ReadTail(job.StdoutPath),
                ReadTail(job.StderrPath))).ToArray(),
            triggers.Select(trigger => new EyeLiveTriggerResult(
                trigger.TriggerId,
                trigger.Incarnation,
                trigger.Kind,
                trigger.State,
                trigger.CreatedAt,
                trigger.DeadlineAt,
                trigger.ProcessId,
                trigger.FilePath,
                trigger.FailureMessage)).ToArray(),
            artifacts.Select(artifact => new EyeLiveArtifactResult(
                artifact.ArtifactId,
                artifact.Incarnation,
                artifact.Kind,
                artifact.Name,
                artifact.SizeBytes,
                artifact.StorageTier,
                artifact.CreatedAt)).ToArray());
    }

    private static string ReadTail(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length == 0)
                return string.Empty;

            var count = (int)Math.Min(stream.Length, TailBytes);
            stream.Seek(-count, SeekOrigin.End);
            var bytes = new byte[count];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0) break;
                offset += read;
            }

            return Encoding.UTF8.GetString(bytes, 0, offset);
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
