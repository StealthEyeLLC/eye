using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace StealthEye.Runtime;

internal sealed class InteractiveTaskProcessLease : IDisposable
{
    private readonly string _taskName;
    private readonly string? _scriptPath;
    private readonly string? _pidPath;
    private readonly string? _exitPath;
    private readonly string? _markerPath;
    private int _disposed;

    internal InteractiveTaskProcessLease(
        string taskName,
        string? scriptPath,
        string? pidPath,
        string? exitPath,
        string? markerPath)
    {
        _taskName = taskName;
        _scriptPath = scriptPath;
        _pidPath = pidPath;
        _exitPath = exitPath;
        _markerPath = markerPath;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        TryRunSchtasks("/End", "/TN", _taskName);
        TryRunSchtasks("/Delete", "/TN", _taskName, "/F");
        TryDelete(_scriptPath);
        TryDelete(_pidPath);
        TryDelete(_exitPath);
        TryDelete(_markerPath);
    }

    private static void TryRunSchtasks(params string[] args)
    {
        try
        {
            _ = InteractiveTaskProcessLauncher.RunSchtasks(args, throwOnFailure: false);
        }
        catch
        {
        }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { File.Delete(path); } catch { }
    }
}

internal sealed record InteractiveTaskProcessLaunch(
    Process Process,
    SafeFileHandle JobHandle,
    InteractiveTaskProcessLease Lease);

internal static class InteractiveTaskProcessLauncher
{
    static InteractiveTaskProcessLauncher()
    {
        CleanupStaleOwnedResidue();
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { CleanupStaleOwnedResidue(); } catch { }
        };
    }

    internal static void CleanupStaleOwnedResidue()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        foreach (var root in new[]
        {
            Path.Combine(programData, "StealthEye", "session-launch"),
            Path.Combine(programData, "StealthEye", "s")
        })
        {
            if (!Directory.Exists(root))
                continue;

            foreach (var marker in Directory.EnumerateFiles(root, "*.task"))
            {
                try
                {
                    var taskName = File.ReadAllText(marker).Trim();
                    if (!string.IsNullOrWhiteSpace(taskName))
                        _ = RunSchtasks(["/Delete", "/TN", taskName, "/F"], throwOnFailure: false);
                }
                catch
                {
                }
            }

            foreach (var path in Directory.EnumerateFiles(root))
            {
                try { File.Delete(path); } catch { }
            }
        }

        var outputRoot = Path.Combine(programData, "StealthEye", "process-output");
        if (Directory.Exists(outputRoot))
        {
            foreach (var path in Directory.EnumerateFiles(outputRoot))
            {
                try { File.Delete(path); } catch { }
            }
        }
    }

    internal static InteractiveTaskProcessLaunch Launch(
        string executablePath,
        string[] arguments,
        TimeSpan timeout,
        string? workingDirectory = null,
        string? stdoutPath = null,
        string? stderrPath = null)
    {
        executablePath = Path.GetFullPath(executablePath);
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("Interactive executable not found.", executablePath);

        var userName = GetActiveUserName();
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "StealthEye",
            "session-launch");
        Directory.CreateDirectory(root);

        var id = Guid.NewGuid().ToString("N");
        var taskName = "StealthEye-Session-" + id;
        var scriptPath = Path.Combine(root, id + ".ps1");
        var pidPath = Path.Combine(root, id + ".pid");
        var exitPath = Path.Combine(root, id + ".exit");
        var markerPath = Path.Combine(root, id + ".task");
        File.WriteAllText(markerPath, taskName, new System.Text.UTF8Encoding(false));

        var powerShell = ResolvePowerShell();
        File.WriteAllText(
            scriptPath,
            BuildLaunchScript(
                executablePath,
                arguments,
                pidPath,
                exitPath,
                workingDirectory,
                stdoutPath,
                stderrPath),
            new System.Text.UTF8Encoding(false));

        var lease = new InteractiveTaskProcessLease(taskName, scriptPath, pidPath, exitPath, markerPath);
        try
        {
            var taskAction =
                ProcessRunner.QuoteArgument(powerShell) +
                " -NoLogo -NoProfile -ExecutionPolicy Bypass -File " +
                ProcessRunner.QuoteArgument(scriptPath);

            RunSchtasks(
                [
                    "/Create",
                    "/TN", taskName,
                    "/SC", "ONCE",
                    "/SD", "12/31/2099",
                    "/ST", "23:59",
                    "/TR", taskAction,
                    "/RU", userName,
                    "/RL", "LIMITED",
                    "/IT",
                    "/F"
                ],
                throwOnFailure: true);

            RunSchtasks(["/Run", "/TN", taskName], throwOnFailure: true);

            var deadline = DateTime.UtcNow + timeout;
            int pid = 0;
            while (DateTime.UtcNow < deadline)
            {
                if (TryReadPublishedPid(pidPath, out pid))
                    break;

                Thread.Sleep(50);
            }

            if (pid <= 0)
            {
                var detail = RunSchtasks(
                    ["/Query", "/TN", taskName, "/V", "/FO", "LIST"],
                    throwOnFailure: false);
                throw new InvalidOperationException(
                    $"Interactive task did not publish a worker PID within {timeout.TotalSeconds:0.#}s. {detail}");
            }

            var process = Process.GetProcessById(pid);
            var job = ProcessRunner.CreateKillOnCloseJob();
            try
            {
                if (!NativeMethods.AssignProcessToJobObject(job, process.Handle))
                    ProcessRunner.ThrowWin32("AssignProcessToJobObject(interactive worker)");
                return new InteractiveTaskProcessLaunch(process, job, lease);
            }
            catch
            {
                job.Dispose();
                process.Dispose();
                throw;
            }
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    internal static InteractiveTaskProcessLaunch LaunchDirect(
        string executablePath,
        string[] arguments,
        TimeSpan timeout)
    {
        executablePath = Path.GetFullPath(executablePath);
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("Interactive executable not found.", executablePath);

        var userName = GetActiveUserName();
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "StealthEye",
            "s");
        Directory.CreateDirectory(root);

        var id = Guid.NewGuid().ToString("N")[..12];
        var taskName = "EyeS-" + id;
        var pidPath = Path.Combine(root, id + ".pid");
        var batchPath = Path.Combine(root, id + ".cmd");
        var markerPath = Path.Combine(root, id + ".task");
        File.WriteAllText(markerPath, taskName, new System.Text.UTF8Encoding(false));
        var directArguments = arguments.Concat(["--pid-file", pidPath]).ToArray();
        var workerCommand = ProcessRunner.BuildCommandLine(executablePath, directArguments);
        File.WriteAllText(
            batchPath,
            string.Join(
                Environment.NewLine,
                "@echo off",
                workerCommand,
                $"schtasks.exe /Delete /TN \"{taskName}\" /F >nul 2>&1",
                $"del /f /q \"{pidPath}\" >nul 2>&1",
                "del /f /q \"%~f0\" >nul 2>&1"),
            new System.Text.UTF8Encoding(false));
        var taskAction = ProcessRunner.BuildCommandLine(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            ["/d", "/c", batchPath]);
        var lease = new InteractiveTaskProcessLease(taskName, batchPath, pidPath, null, markerPath);

        try
        {
            RunSchtasks(
                [
                    "/Create",
                    "/TN", taskName,
                    "/SC", "ONCE",
                    "/SD", "12/31/2099",
                    "/ST", "23:59",
                    "/TR", taskAction,
                    "/RU", userName,
                    "/RL", "LIMITED",
                    "/IT",
                    "/F"
                ],
                throwOnFailure: true);
            RunSchtasks(["/Run", "/TN", taskName], throwOnFailure: true);

            var deadline = DateTime.UtcNow + timeout;
            var pid = 0;
            while (DateTime.UtcNow < deadline)
            {
                if (TryReadPublishedPid(pidPath, out pid))
                    break;

                Thread.Sleep(20);
            }

            if (pid <= 0)
            {
                var detail = RunSchtasks(
                    ["/Query", "/TN", taskName, "/V", "/FO", "LIST"],
                    throwOnFailure: false);
                throw new InvalidOperationException(
                    $"Direct interactive task did not publish a PID within {timeout.TotalSeconds:0.#}s. {detail}");
            }

            var process = Process.GetProcessById(pid);
            var job = ProcessRunner.CreateKillOnCloseJob();
            try
            {
                if (!NativeMethods.AssignProcessToJobObject(job, process.Handle))
                    ProcessRunner.ThrowWin32("AssignProcessToJobObject(direct interactive worker)");
                return new InteractiveTaskProcessLaunch(process, job, lease);
            }
            catch
            {
                job.Dispose();
                process.Dispose();
                throw;
            }
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
    private static bool TryReadPublishedPid(string pidPath, out int pid)
    {
        pid = 0;
        if (!File.Exists(pidPath))
            return false;

        try
        {
            using var pidStream = new FileStream(
                pidPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(pidStream);
            var text = reader.ReadToEnd().Trim();
            return int.TryParse(text, out pid) && pid > 0;
        }
        catch (IOException)
        {
            return false;
        }
    }
    internal static string RunSchtasks(string[] args, bool throwOnFailure)
    {
        var executable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "schtasks.exe");
        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Unable to start schtasks.exe.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(15_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("schtasks.exe timed out.");
        }

        var detail = string.Join(
            " ",
            new[] { stdout.Trim(), stderr.Trim() }.Where(x => x.Length > 0));
        if (throwOnFailure && process.ExitCode != 0)
            throw new InvalidOperationException(
                $"schtasks.exe exited {process.ExitCode}: {detail}");

        return detail;
    }

    private static string GetActiveUserName()
    {
        var sessionId = ProcessRunner.FindActiveSessionId();
        if (!NativeMethods.WTSQueryUserToken((uint)sessionId, out var token))
            ProcessRunner.ThrowWin32("WTSQueryUserToken(interactive task)");
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            return identity.Name;
    }

    private static string ResolvePowerShell()
    {
        var pwsh = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "PowerShell",
            "7",
            "pwsh.exe");
        if (File.Exists(pwsh))
            return pwsh;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
    }

    private static string BuildLaunchScript(
        string executablePath,
        string[] arguments,
        string pidPath,
        string exitPath,
        string? workingDirectory,
        string? stdoutPath,
        string? stderrPath)
    {
        static string Ps(string value) => "'" + value.Replace("'", "''") + "'";
        static string PsNullable(string? value) => value is null ? "$null" : Ps(value);

        var args = string.Join(",", arguments.Select(Ps));
        return string.Join(
            Environment.NewLine,
            "$ErrorActionPreference='Stop'",
            "$exe=" + Ps(executablePath),
            "$args=@(" + args + ")",
            "$pidFile=" + Ps(pidPath),
            "$exitFile=" + Ps(exitPath),
            "$workingDirectory=" + PsNullable(workingDirectory),
            "$stdoutFile=" + PsNullable(stdoutPath),
            "$stderrFile=" + PsNullable(stderrPath),
            "$psi=[Diagnostics.ProcessStartInfo]::new()",
            "$psi.FileName=$exe",
            "$psi.UseShellExecute=$false",
            "$psi.CreateNoWindow=$true",
            "foreach($arg in $args){[void]$psi.ArgumentList.Add($arg)}",
            "if($workingDirectory){$psi.WorkingDirectory=$workingDirectory}",
            "if($stdoutFile){$psi.RedirectStandardOutput=$true}",
            "if($stderrFile){$psi.RedirectStandardError=$true}",
            "$p=[Diagnostics.Process]::new()",
            "$p.StartInfo=$psi",
            "if(-not $p.Start()){throw 'Process.Start returned false.'}",
            "[IO.File]::WriteAllText($pidFile,[string]$p.Id)",
            "$stdoutStream=$null",
            "$stderrStream=$null",
            "$stdoutCopy=$null",
            "$stderrCopy=$null",
            "try {",
            "  if($stdoutFile){",
            "    $stdoutStream=[IO.File]::Open($stdoutFile,[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::ReadWrite)",
            "    $stdoutCopy=$p.StandardOutput.BaseStream.CopyToAsync($stdoutStream)",
            "  }",
            "  if($stderrFile){",
            "    $stderrStream=[IO.File]::Open($stderrFile,[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::ReadWrite)",
            "    $stderrCopy=$p.StandardError.BaseStream.CopyToAsync($stderrStream)",
            "  }",
            "  $p.WaitForExit()",
            "  if($stdoutCopy){$stdoutCopy.GetAwaiter().GetResult();$stdoutStream.Flush()}",
            "  if($stderrCopy){$stderrCopy.GetAwaiter().GetResult();$stderrStream.Flush()}",
            "  [IO.File]::WriteAllText($exitFile,[string]$p.ExitCode)",
            "  exit $p.ExitCode",
            "} finally {",
            "  if($stdoutStream){$stdoutStream.Dispose()}",
            "  if($stderrStream){$stderrStream.Dispose()}",
            "  $p.Dispose()",
            "}"
        );
    }
}
