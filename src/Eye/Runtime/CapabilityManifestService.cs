using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using StealthEye.Contract;

namespace StealthEye.Runtime;

public sealed class CapabilityManifestService
{
    private readonly string? suiteCatalogPath;

    public CapabilityManifestService()
    {
    }

    public CapabilityManifestService(string suiteCatalogPath)
    {
        this.suiteCatalogPath = string.IsNullOrWhiteSpace(suiteCatalogPath)
            ? throw new ArgumentException("Suite catalog path is required.", nameof(suiteCatalogPath))
            : Path.GetFullPath(suiteCatalogPath);
    }
    private static readonly SoftwareProbe[] SoftwareProbes =
    [
        new("git", ["git.exe"]),
        new("github-cli", ["gh.exe"]),
        new("powershell", ["pwsh.exe", "powershell.exe"]),
        new("wsl", ["wsl.exe"]),
        new("winget", ["winget.exe"]),
        new("ripgrep", ["rg.exe"]),
        new("ast-grep", ["ast-grep.exe", "sg.exe"]),
        new("duckdb", ["duckdb.exe"]),
        new("ffmpeg", ["ffmpeg.exe"]),
        new("ffprobe", ["ffprobe.exe"]),
        new("whisper.cpp", ["whisper-cli.exe", "whisper.exe"]),
        new("imagemagick", ["magick.exe"]),
        new("tesseract", ["tesseract.exe"])
    ];

    private static readonly AdapterTemplate[] OperationTemplates =
    [
        new("windows.bits", "transfer", "Windows BITS", NativeDll("qmgr.dll"), false,
            "Reboot-resilient background transfer; use when a real transfer benefits from BITS."),
        new("windows.vss", "storage", "Windows VSS", NativeDll("vssapi.dll"), false,
            "Consistent reads/snapshots of changing or locked volumes."),
        new("windows.restart_manager", "process", "Windows Restart Manager", NativeDll("rstrtmgr.dll"), false,
            "Discover and coordinate processes locking files/resources."),
        new("windows.copyfile2", "storage", "Windows CopyFile2", OperatingSystem.IsWindows(), false,
            "Native progress/cancel-aware file copy primitive."),
        new("windows.process_snapshot", "process", "Windows Process Snapshotting", OperatingSystem.IsWindowsVersionAtLeast(6, 3), false,
            "PSS snapshot primitive for bounded process inspection."),
        new("windows.virtual_disk", "storage", "Windows Virtual Disk API", NativeDll("virtdisk.dll"), false,
            "VHD/VHDX attach/create/inspect operations."),
        new("windows.refs_clone", "storage", "ReFS block cloning", HasReadyDriveFormat("ReFS"), false,
            "Fast same-volume block clone when the source/destination volume is ReFS."),
        new("windows.services", "system", "Windows SCM", OperatingSystem.IsWindows(), false,
            "Service query/control through native Windows mechanisms."),
        new("windows.task_scheduler", "system", "Windows Task Scheduler", OperatingSystem.IsWindows(), false,
            "Native scheduled/persistent execution."),
        new("windows.ocr", "vision", "Windows Media OCR", OperatingSystem.IsWindowsVersionAtLeast(10), false,
            "On-demand OCR fallback used by desktop capture."),
        new("git", "code", "Git CLI", HasSoftware("git"), true,
            "Repository inspection/mutation through installed Git."),
        new("github-cli", "provider", "GitHub CLI", HasSoftware("github-cli"), true,
            "Optional local GitHub CLI; ChatGPT may prefer the connected GitHub authority."),
        new("powershell", "system", "PowerShell", HasSoftware("powershell"), true,
            "Deterministic Windows scripting and system administration."),
        new("wsl", "system", "Windows Subsystem for Linux", HasSoftware("wsl"), true,
            "Linux execution through the installed WSL boundary."),
        new("winget", "packages", "Windows Package Manager", HasSoftware("winget"), true,
            "Package discovery/install when available to the service/user context."),
        new("ripgrep", "code", "ripgrep", HasSoftware("ripgrep"), true,
            "Fast textual code/content search."),
        new("ast-grep", "code", "ast-grep", HasSoftware("ast-grep"), true,
            "Structural code search/rewrites; optional and installed only when a real task benefits."),
        new("language-server", "code", "On-demand language server", false, true,
            "No permanent LSP daemon; start an appropriate installed server only for tasks that need semantic code analysis."),
        new("markitdown", "documents", "MarkItDown", false, true,
            "Optional document extraction; install/use only when a real document task benefits."),
        new("pdfpig", "documents", "PdfPig", false, true,
            "Optional native PDF text/geometry extraction."),
        new("openxml", "documents", "Open XML SDK", false, true,
            "Optional Office package manipulation."),
        new("closedxml", "documents", "ClosedXML", false, true,
            "Optional spreadsheet manipulation."),
        new("docling", "documents", "Docling", false, true,
            "Heavy document extraction kept on-demand only."),
        new("duckdb", "data", "DuckDB", HasSoftware("duckdb"), true,
            "Embedded/on-demand analytical SQL."),
        new("naudio", "audio", "NAudio", false, true,
            "Optional WASAPI/audio capture library; keep non-resident."),
        new("whisper.cpp", "audio", "whisper.cpp", HasSoftware("whisper.cpp"), true,
            "Short-lived local transcription when installed and useful."),
        new("ffmpeg", "media", "FFmpeg", HasSoftware("ffmpeg"), true,
            "Media probe/transform adapter when installed."),
        new("onnx-runtime", "vision", "ONNX Runtime", false, true,
            "Optional local model runtime used only for measured workloads."),
        new("opencv", "vision", "OpenCV", false, true,
            "Optional local vision adapter used only for measured workloads.")
    ];

    public MachineDescribeResult DescribeMachine()
    {
        var memory = Memory();
        var power = Power();
        var drives = DescribeVolumes().Volumes;
        return new MachineDescribeResult(
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            memory.TotalBytes,
            memory.AvailableBytes,
            power.AcOnline,
            power.BatteryPercent,
            drives,
            FindSoftware(null).Software,
            OperationList().Operations);
    }

    public SessionDescribeResult DescribeSession()
    {
        var activeSessionId = TryActiveSessionId();
        return new SessionDescribeResult(
            Process.GetCurrentProcess().SessionId,
            WindowsIdentity.GetCurrent().Name,
            activeSessionId,
            TryActiveUser(activeSessionId));
    }

    public VolumeDescribeResult DescribeVolumes() =>
        new(DriveInfo.GetDrives()
            .Select(ToVolume)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray());

    public SoftwareFindResult FindSoftware(string? query)
    {
        var items = SoftwareProbes
            .Select(ResolveSoftware)
            .Where(x => string.IsNullOrWhiteSpace(query) ||
                        x.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) ||
                        (x.Path?.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToArray();
        return new SoftwareFindResult(items);
    }

    public SoftwareVersionResult SoftwareVersion(string name)
    {
        var match = SoftwareProbes.FirstOrDefault(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            throw new ArgumentException($"Unknown software adapter: {name}", nameof(name));
        var resolved = ResolveSoftware(match);
        return new SoftwareVersionResult(
            resolved.Name,
            resolved.Available,
            resolved.Path,
            resolved.Version);
    }

    public OperationListResult OperationList() =>
        new(AllOperationTemplates()
            .Select(ToOperation)
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToArray());

    public OperationDescribeResult OperationDescribe(string name)
    {
        var match = AllOperationTemplates().FirstOrDefault(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            throw new ArgumentException($"Unknown operation manifest: {name}", nameof(name));
        return new OperationDescribeResult(ToOperation(match));
    }

    private IEnumerable<AdapterTemplate> AllOperationTemplates() =>
        OperationTemplates.Concat(LoadSuiteOperationTemplates());

    private AdapterTemplate[] LoadSuiteOperationTemplates()
    {
        var programDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "StealthEye",
            "suite-capabilities.json");
        var packagedPath = Path.Combine(AppContext.BaseDirectory, "suite-capabilities.json");
        var path = suiteCatalogPath ??
            (File.Exists(programDataPath) ? programDataPath : packagedPath);

        if (!File.Exists(path))
            return [];

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("apps", out var apps) ||
                apps.ValueKind != JsonValueKind.Array)
                return [];

            var result = new List<AdapterTemplate>();
            foreach (var app in apps.EnumerateArray())
            {
                var id = JsonString(app, "id");
                var manifestName = JsonString(app, "manifest_name");
                if (string.IsNullOrWhiteSpace(manifestName) && !string.IsNullOrWhiteSpace(id))
                    manifestName = $"suite.{id}";
                if (string.IsNullOrWhiteSpace(manifestName))
                    continue;

                var category = JsonString(app, "category") ?? "suite";
                var provider = JsonString(app, "provider") ?? "STEALTHEYE Suite";
                var authority = JsonString(app, "authority") ?? "eye";
                var summary = JsonString(app, "summary") ?? "STEALTHEYE suite capability.";
                var optionalExternal = JsonBool(app, "optional_external") ?? false;
                var configured = JsonBool(app, "configured") ?? true;
                var localPath = JsonString(app, "available_local_path");

                var available = optionalExternal
                    ? configured
                    : string.IsNullOrWhiteSpace(localPath) ||
                      File.Exists(localPath) ||
                      Directory.Exists(localPath);

                var detail =
                    $"{summary} Authority={authority}. " +
                    $"Canonical recipe={path}#{id}. " +
                    "Read the full recipe before first use; prefer its preferred_control; " +
                    "verify the listed postconditions; use its recovery path before retrying unknown outcomes.";

                result.Add(new AdapterTemplate(
                    manifestName,
                    category,
                    provider,
                    available,
                    optionalExternal,
                    detail));
            }

            return result.ToArray();
        }
        catch
        {
            // A bad optional suite catalog must never take down the core capability manifest.
            return [];
        }
    }

    private static string? JsonString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? JsonBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static OperationManifestResult ToOperation(AdapterTemplate x) =>
        new(
            x.Name,
            x.Category,
            x.Provider,
            x.Available,
            x.OptionalExternal,
            x.Detail);

    private static SoftwareManifestResult ResolveSoftware(SoftwareProbe probe)
    {
        foreach (var candidate in probe.Executables)
        {
            var path = ResolveExecutable(candidate);
            if (path is null)
                continue;

            string? version = null;
            try
            {
                version = FileVersionInfo.GetVersionInfo(path).FileVersion;
            }
            catch { }

            return new SoftwareManifestResult(probe.Name, true, path, version);
        }

        return new SoftwareManifestResult(probe.Name, false, null, null);
    }

    private static string? ResolveExecutable(string executable)
    {
        if (Path.IsPathRooted(executable))
            return File.Exists(executable) ? Path.GetFullPath(executable) : null;

        foreach (var segment in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(segment, executable);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch { }
        }

        return null;
    }

    private static bool HasSoftware(string name)
    {
        var probe = SoftwareProbes.First(x => string.Equals(x.Name, name, StringComparison.Ordinal));
        return ResolveSoftware(probe).Available;
    }

    private static bool NativeDll(string name) =>
        File.Exists(Path.Combine(Environment.SystemDirectory, name));

    private static bool HasReadyDriveFormat(string format) =>
        DriveInfo.GetDrives().Any(d =>
        {
            try { return d.IsReady && string.Equals(d.DriveFormat, format, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        });

    private static VolumeManifestResult ToVolume(DriveInfo drive)
    {
        try
        {
            if (!drive.IsReady)
                return new VolumeManifestResult(
                    drive.Name,
                    drive.DriveType.ToString(),
                    false,
                    null,
                    null,
                    null,
                    null);

            return new VolumeManifestResult(
                drive.Name,
                drive.DriveType.ToString(),
                true,
                drive.DriveFormat,
                drive.VolumeLabel,
                drive.TotalSize,
                drive.AvailableFreeSpace);
        }
        catch
        {
            return new VolumeManifestResult(
                drive.Name,
                drive.DriveType.ToString(),
                false,
                null,
                null,
                null,
                null);
        }
    }

    private static int? TryActiveSessionId()
    {
        try { return ProcessRunner.FindActiveSessionId(); }
        catch { return null; }
    }

    private static string? TryActiveUser(int? sessionId)
    {
        if (sessionId is null)
            return null;
        try
        {
            if (!NativeMethods.WTSQueryUserToken((uint)sessionId.Value, out var token))
                return null;
            using (token)
            using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
                return identity.Name;
        }
        catch { return null; }
    }

    private static (long TotalBytes, long AvailableBytes) Memory()
    {
        var status = new MemoryStatusEx();
        return Native.GlobalMemoryStatusEx(ref status)
            ? ((long)status.TotalPhys, (long)status.AvailPhys)
            : (0, 0);
    }

    private static (bool? AcOnline, int? BatteryPercent) Power()
    {
        if (!Native.GetSystemPowerStatus(out var status))
            return (null, null);

        var ac = status.AcLineStatus switch
        {
            0 => false,
            1 => true,
            _ => (bool?)null
        };
        int? battery = status.BatteryLifePercent == 255
            ? null
            : status.BatteryLifePercent;
        return (ac, battery);
    }

    private sealed record SoftwareProbe(string Name, string[] Executables);
    private sealed record AdapterTemplate(
        string Name,
        string Category,
        string Provider,
        bool Available,
        bool OptionalExternal,
        string Detail);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        internal uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        internal uint MemoryLoad;
        internal ulong TotalPhys;
        internal ulong AvailPhys;
        internal ulong TotalPageFile;
        internal ulong AvailPageFile;
        internal ulong TotalVirtual;
        internal ulong AvailVirtual;
        internal ulong AvailExtendedVirtual;

        public MemoryStatusEx() { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        internal byte AcLineStatus;
        internal byte BatteryFlag;
        internal byte BatteryLifePercent;
        internal byte SystemStatusFlag;
        internal uint BatteryLifeTime;
        internal uint BatteryFullLifeTime;
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
    }
}
