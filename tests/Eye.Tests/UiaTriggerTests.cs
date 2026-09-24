using System.Text.Json;
using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

[Collection("Interactive desktop")]
public sealed class UiaTriggerTests : IDisposable
{
    private string? _work;

    [Fact]
    public async Task PendingUiaTrigger_ReattachesAfterBrokerRestartAndEmitsStablePayload()
    {
        var runner = new ProcessRunner();
        var tempResult = await runner.RunAsync(new RunRequest
        {
            Context = "user",
            FileName = "powershell.exe",
            Arguments = ["-NoProfile", "-Command", "[Console]::Write($env:TEMP)"],
            TimeoutMs = 10_000
        });
        Assert.Equal(0, tempResult.ExitCode);
        var userTemp = tempResult.Stdout.Trim();
        Assert.True(Directory.Exists(userTemp));

        _work = Path.Combine(userTemp, "eye-uia-trigger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_work);
        var script = Path.Combine(_work, "form.ps1");
        var title = "Eye UIA Trigger " + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(script, BuildScript(title));

        var stateRoot = Path.Combine(Path.GetTempPath(), "eye-uia-trigger-state-" + Guid.NewGuid().ToString("N"));
        var jobs = new JobStore(stateRoot, Path.Combine(Path.GetTempPath(), "eye-uia-trigger-spool-" + Guid.NewGuid().ToString("N")));
        var manager = new JobManager(jobs, runner);
        var job = manager.Start(new RunRequest
        {
            Context = "user",
            FileName = "powershell.exe",
            Arguments = ["-NoProfile", "-STA", "-ExecutionPolicy", "Bypass", "-File", script],
            TimeoutMs = 30_000
        });

        try
        {
            var contract = EyeContractCatalog.Load();
            var workers = new SessionWorkerManager(WorkerExecutable(), contract.WorkerProtocolVersion);
            var windowStore = new DesktopWindowStore(jobs);
            var desktop = new DesktopObservationService(workers, windowStore);
            var elementStore = new UiaElementStore(jobs);
            var query = new UiaQueryService(windowStore, workers, elementStore);
            var actions = new UiaActionService(windowStore, elementStore, workers);
            var source = new UiaTriggerSource(windowStore, elementStore, workers);
            var triggerStore = new TriggerStore(jobs);

            DesktopWindowState? window = null;
            for (var i = 0; i < 50 && window is null; i++)
            {
                await Task.Delay(100);
                var observed = await desktop.ObserveAsync();
                window = observed.Windows.FirstOrDefault(x => string.Equals(x.Title, title, StringComparison.Ordinal));
            }
            Assert.NotNull(window);

            var tree = await query.QueryAsync(window!.WindowId, maxDepth: 5, maxNodes: 300);
            var button = tree.Elements.FirstOrDefault(x =>
                string.Equals(x.AutomationId, "focusButton", StringComparison.Ordinal) ||
                (x.ControlType.EndsWith(".Button", StringComparison.Ordinal) && string.Equals(x.Name, "Focus target", StringComparison.Ordinal)));
            Assert.NotNull(button);

            string triggerId;
            await using (var first = new TriggerBroker(triggerStore, source))
            {
                await first.InitializeAsync();
                var created = first.CreateUiaChange(window.WindowId, button!.ElementId, ["focus"], 10_000);
                triggerId = created.TriggerId;
                Assert.Equal(TriggerKinds.UiaChange, created.Kind);
                Assert.False(string.IsNullOrWhiteSpace(created.RegistrationJson));
                await Task.Delay(150);
            }

            await using var second = new TriggerBroker(triggerStore, source);
            await second.InitializeAsync();
            Assert.Equal(TriggerStates.Pending, second.Status(triggerId).State);
            await second.WaitUntilArmedAsync(triggerId);

            var acted = await actions.ActAsync(button!.ElementId, "focus");
            Assert.True(acted.Completed);

            var waited = await second.WaitAsync(triggerId, 5_000);
            Assert.False(waited.WaitTimedOut);
            Assert.Equal(TriggerStates.Satisfied, waited.Trigger.State);

            var read = second.Read(triggerId, 0, 10);
            var evt = Assert.Single(read.Events);
            Assert.Equal("uia_changed", evt.EventType);
            Assert.DoesNotContain("runtime_id", evt.PayloadJson, StringComparison.OrdinalIgnoreCase);
            using var payload = JsonDocument.Parse(evt.PayloadJson);
            Assert.Equal("focus", payload.RootElement.GetProperty("event_type").GetString());
            Assert.Equal(window.WindowId, payload.RootElement.GetProperty("window_id").GetString());
            Assert.Equal(window.Incarnation, payload.RootElement.GetProperty("window_incarnation").GetInt64());
            Assert.Equal(button.ElementId, payload.RootElement.GetProperty("element_id").GetString());
            Assert.Equal(button.Incarnation, payload.RootElement.GetProperty("element_incarnation").GetInt64());
            Assert.True(read.Eof);
        }
        finally
        {
            var current = manager.Status(job.JobId);
            if (!JobStates.IsTerminal(current.State))
                await manager.CancelAsync(job.JobId);
        }
    }

    private static string BuildScript(string title)
    {
        static string Q(string value) => value.Replace("'", "''", StringComparison.Ordinal);
        return string.Join(Environment.NewLine,
            "Add-Type -AssemblyName System.Windows.Forms",
            "$form = New-Object System.Windows.Forms.Form",
            $"$form.Text = '{Q(title)}'",
            "$form.Width = 420",
            "$form.Height = 160",
            "$text = New-Object System.Windows.Forms.TextBox",
            "$text.Name = 'inputBox'",
            "$text.Text = 'before'",
            "$text.Left = 20",
            "$text.Top = 20",
            "$text.Width = 350",
            "$form.Controls.Add($text)",
            "$button = New-Object System.Windows.Forms.Button",
            "$button.Name = 'focusButton'",
            "$button.Text = 'Focus target'",
            "$button.Left = 20",
            "$button.Top = 60",
            "$button.Width = 120",
            "$form.Controls.Add($button)",
            "[void]$form.ShowDialog()");
    }

    private static string WorkerExecutable()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Eye.slnx")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "src", "Eye.Worker", "bin", "Release", "net10.0-windows", "eye-worker.exe");
    }

    public void Dispose()
    {
        if (_work is not null && Directory.Exists(_work))
            Directory.Delete(_work, recursive: true);
    }
}