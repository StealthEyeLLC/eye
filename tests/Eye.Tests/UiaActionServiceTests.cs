using System.Text.Json;
using StealthEye.Contract;
using StealthEye.Runtime;

namespace Eye.Tests;

[Collection("Interactive desktop")]
public sealed class UiaActionServiceTests : IDisposable
{
    private string? _work;

    [Fact]
    public async Task FocusSetValueAndInvoke_WorkOnActiveUserWinForms()
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
        Assert.True(Directory.Exists(userTemp), $"Active-user temp directory missing: {userTemp}");

        _work = Path.Combine(userTemp, "eye-uia-action-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_work);
        var marker = Path.Combine(_work, "result.txt");
        var script = Path.Combine(_work, "form.ps1");
        var title = "Eye UIA Action " + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(script, BuildScript(title, marker));

        var jobs = new JobStore(
            Path.Combine(Path.GetTempPath(), "eye-uia-action-state-" + Guid.NewGuid().ToString("N")),
            Path.Combine(Path.GetTempPath(), "eye-uia-action-spool-" + Guid.NewGuid().ToString("N")));
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
            var dispatcher = new EyeDispatcher(
                manager,
                new ArtifactStore(jobs),
                uiaActionService: actions);

            DesktopWindowState? window = null;
            for (var i = 0; i < 50 && window is null; i++)
            {
                await Task.Delay(100);
                var observed = await desktop.ObserveAsync();
                window = observed.Windows.FirstOrDefault(x => string.Equals(x.Title, title, StringComparison.Ordinal));
            }
            Assert.NotNull(window);

            var tree = await query.QueryAsync(window!.WindowId, maxDepth: 5, maxNodes: 300);
            var edit = tree.Elements.FirstOrDefault(x =>
                string.Equals(x.AutomationId, "inputBox", StringComparison.Ordinal) ||
                x.ControlType.EndsWith(".Edit", StringComparison.Ordinal));
            var button = tree.Elements.FirstOrDefault(x =>
                string.Equals(x.AutomationId, "submitButton", StringComparison.Ordinal) ||
                (x.ControlType.EndsWith(".Button", StringComparison.Ordinal) && string.Equals(x.Name, "Submit", StringComparison.Ordinal)));
            Assert.NotNull(edit);
            Assert.NotNull(button);

            var focused = JsonSerializer.SerializeToElement(await dispatcher.ExecuteAsync(
                EyeEffectClass.Interact,
                "ui.act",
                JsonSerializer.SerializeToElement(new UiActArgs(edit!.ElementId, "focus"))));
            Assert.True(focused.GetProperty("ok").GetBoolean(), focused.ToString());
            Assert.True(focused.GetProperty("result").GetProperty("completed").GetBoolean());

            var changed = JsonSerializer.SerializeToElement(await dispatcher.ExecuteAsync(
                EyeEffectClass.Interact,
                "ui.act",
                JsonSerializer.SerializeToElement(new UiActArgs(edit.ElementId, "set_value", "changed-by-eye"))));
            Assert.True(changed.GetProperty("ok").GetBoolean(), changed.ToString());

            var invoked = JsonSerializer.SerializeToElement(await dispatcher.ExecuteAsync(
                EyeEffectClass.Interact,
                "ui.act",
                JsonSerializer.SerializeToElement(new UiActArgs(button!.ElementId, "invoke"))));
            Assert.True(invoked.GetProperty("ok").GetBoolean(), invoked.ToString());

            var invalid = JsonSerializer.SerializeToElement(await dispatcher.ExecuteAsync(
                EyeEffectClass.Interact,
                "ui.act",
                JsonSerializer.SerializeToElement(new UiActArgs("element_missing", "toggle"))));
            Assert.False(invalid.GetProperty("ok").GetBoolean());
            Assert.Equal("invalid_argument", invalid.GetProperty("error").GetProperty("code").GetString());

            var wrongFacade = JsonSerializer.SerializeToElement(await dispatcher.ExecuteAsync(
                EyeEffectClass.Inspect,
                "ui.act",
                JsonSerializer.SerializeToElement(new UiActArgs(edit.ElementId, "focus"))));
            Assert.False(wrongFacade.GetProperty("ok").GetBoolean());
            Assert.Equal("wrong_tool", wrongFacade.GetProperty("error").GetProperty("code").GetString());
            Assert.Equal("eye_interact", wrongFacade.GetProperty("error").GetProperty("expected").GetProperty("tool").GetString());
            var waited = await manager.WaitAsync(job.JobId, 10_000);
            Assert.False(waited.WaitTimedOut);
            Assert.Equal(JobStates.Completed, waited.Job.State);
            Assert.True(File.Exists(marker));
            Assert.Equal("changed-by-eye", await File.ReadAllTextAsync(marker));
        }
        finally
        {
            var current = manager.Status(job.JobId);
            if (!JobStates.IsTerminal(current.State))
                await manager.CancelAsync(job.JobId);
        }
    }

    private static string BuildScript(string title, string marker)
    {
        static string Q(string value) => value.Replace("'", "''", StringComparison.Ordinal);
        return string.Join(Environment.NewLine,
            "Add-Type -AssemblyName System.Windows.Forms",
            "$form = New-Object System.Windows.Forms.Form",
            $"$form.Text = '{Q(title)}'",
            "$form.Width = 420",
            "$form.Height = 180",
            "$text = New-Object System.Windows.Forms.TextBox",
            "$text.Name = 'inputBox'",
            "$text.Text = 'before'",
            "$text.Left = 20",
            "$text.Top = 20",
            "$text.Width = 350",
            "$button = New-Object System.Windows.Forms.Button",
            "$button.Name = 'submitButton'",
            "$button.Text = 'Submit'",
            "$button.Left = 20",
            "$button.Top = 60",
            "$button.Width = 120",
            "$button.Add_Click({",
            $"    [IO.File]::WriteAllText('{Q(marker)}', $text.Text)",
            "    $form.Close()",
            "})",
            "$form.Controls.Add($text)",
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