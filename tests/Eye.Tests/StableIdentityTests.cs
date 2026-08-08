using System.Text;
using StealthEye.Runtime;

namespace Eye.Tests;

public sealed class StableIdentityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eye-identity-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void JobIdentity_SurvivesStoreReopen()
    {
        var state = Path.Combine(_root, "state");
        var spool = Path.Combine(_root, "spool", "jobs");
        var first = new JobStore(state, spool);
        var paths = first.AllocatePaths("job_identity_test");
        var created = first.Create("job_identity_test", new RunRequest { FileName = "cmd.exe" }, paths);
        first.Finish("job_identity_test", JobStates.Completed, new ProcessRunResult(1, 0, false, "", "", "system", "test", 0));

        var reopened = new JobStore(state, spool).GetRequired(created.JobId);
        Assert.Equal(created.JobId, reopened.JobId);
        Assert.Equal(created.Incarnation, reopened.Incarnation);
        Assert.Equal(1, reopened.Incarnation);
    }

    [Fact]
    public async Task ArtifactIdentity_SurvivesStoreReopen()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "identity.txt");
        await File.WriteAllTextAsync(source, "identity", new UTF8Encoding(false));
        var state = Path.Combine(_root, "state");
        var spool = Path.Combine(_root, "spool", "jobs");
        var jobs = new JobStore(state, spool);
        var created = await new ArtifactStore(jobs).ImportFileAsync(source, "text", "text/plain", "identity.txt", "identity-test");

        var reopened = new ArtifactStore(new JobStore(state, spool)).Info(created.ArtifactId);
        Assert.Equal(created.ArtifactId, reopened.ArtifactId);
        Assert.Equal(created.Incarnation, reopened.Incarnation);
        Assert.Equal(1, reopened.Incarnation);
        Assert.Equal(created.Sha256, reopened.Sha256);
    }

    [Fact]
    public async Task JobObservationCursor_AdvancesWithoutChangingIdentity()
    {
        var state = Path.Combine(_root, "state");
        var spool = Path.Combine(_root, "spool", "jobs");
        var store = new JobStore(state, spool);
        var paths = store.AllocatePaths("job_cursor_test");
        store.Create("job_cursor_test", new RunRequest { FileName = "cmd.exe" }, paths);
        await File.WriteAllTextAsync(paths.Stdout, "alpha", new UTF8Encoding(false));
        store.Finish("job_cursor_test", JobStates.Completed, new ProcessRunResult(1, 0, false, "", "", "system", "test", 0));
        var manager = new JobManager(store, new ProcessRunner());

        var first = await manager.ReadAsync("job_cursor_test", "stdout", 0, 2);
        var second = await manager.ReadAsync("job_cursor_test", "stdout", first.NextCursor, 16);
        var status = manager.Status("job_cursor_test");

        Assert.Equal("job_cursor_test", first.JobId);
        Assert.Equal(first.JobId, second.JobId);
        Assert.Equal(2, first.NextCursor);
        Assert.Equal(first.NextCursor, second.Cursor);
        Assert.True(second.NextCursor > first.NextCursor);
        Assert.Equal(1, status.Incarnation);
    }

    [Fact]
    public void TerminalAttachCursor_TracksCurrentOutputWithoutChangingIdentity()
    {
        var store = new JobStore(Path.Combine(_root, "state"), Path.Combine(_root, "spool", "jobs"));
        var paths = store.AllocatePaths("job_attach_cursor_test");
        store.Create(
            "job_attach_cursor_test",
            new RunRequest { FileName = "powershell.exe" },
            paths,
            terminal: true,
            columns: 120,
            rows: 30);
        File.WriteAllText(paths.Stdout, "abc", new UTF8Encoding(false));
        var manager = new JobManager(store, new ProcessRunner());
        var first = manager.Attach("job_attach_cursor_test");
        File.AppendAllText(paths.Stdout, "def", new UTF8Encoding(false));
        var second = manager.Attach("job_attach_cursor_test");

        Assert.Equal(first.Job.JobId, second.Job.JobId);
        Assert.Equal(first.Job.Incarnation, second.Job.Incarnation);
        Assert.Equal(3, first.StdoutCursor);
        Assert.Equal(6, second.StdoutCursor);
        Assert.Equal(0, second.StderrCursor);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
