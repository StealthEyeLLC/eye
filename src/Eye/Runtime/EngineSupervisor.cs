using System.Text.Json;
using StealthEye.Contract;

namespace StealthEye.Runtime;

public sealed record EngineSelectionState(
    string? ActiveVersion,
    string? PreviousVersion);

public sealed record EngineSupervisorStatus(
    string State,
    string? ActiveVersion,
    string? PreviousVersion,
    string? EngineVersion,
    int? ProcessId,
    string? LastError);

public sealed class EngineSupervisor : IAsyncDisposable
{
    private static readonly TimeSpan CrashLoopWindow = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly EyeContractCatalog _contract;
    private EngineSelectionState _selection;
    private EngineInstance? _active;
    private string? _lastError;
    private DateTimeOffset _activeStartedAt;
    private int _rapidCrashCount;
    private long _activeGeneration;
    private int _disposed;

    public EngineSupervisor(
        string? stateRoot = null,
        string? engineRoot = null,
        EyeContractCatalog? contract = null)
    {
        StateRoot = stateRoot
            ?? Environment.GetEnvironmentVariable("EYE_STATE_ROOT")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "StealthEye");
        EngineRoot = engineRoot
            ?? Environment.GetEnvironmentVariable("EYE_ENGINE_ROOT")
            ?? Path.Combine(StateRoot, "engines");
        SelectorPath = Path.Combine(StateRoot, "engine-state.json");
        _contract = contract ?? EyeContractCatalog.Load();

        Directory.CreateDirectory(StateRoot);
        Directory.CreateDirectory(EngineRoot);
        try
        {
            _selection = LoadSelection();
        }
        catch (Exception ex)
        {
            _selection = new EngineSelectionState(null, null);
            _lastError = ex.Message;
        }
    }

    public string StateRoot { get; }
    public string EngineRoot { get; }
    public string SelectorPath { get; }

    public EngineSupervisorStatus Status()
    {
        ThrowIfDisposed();
        var active = _active;
        if (active is not null && !active.HasExited)
        {
            return new EngineSupervisorStatus(
                "healthy",
                _selection.ActiveVersion,
                _selection.PreviousVersion,
                active.Handshake.EngineVersion,
                active.ProcessId,
                _lastError);
        }

        if (_selection.ActiveVersion is null)
            return new EngineSupervisorStatus(_lastError is null ? "not_configured" : "unavailable", null, _selection.PreviousVersion, null, null, _lastError);

        return new EngineSupervisorStatus(
            "unavailable",
            _selection.ActiveVersion,
            _selection.PreviousVersion,
            active?.Handshake.EngineVersion,
            null,
            _lastError);
    }

    public async Task<EngineSupervisorStatus> InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_active is not null && !_active.HasExited)
                return Status();
            if (_selection.ActiveVersion is null)
                return Status();

            try
            {
                var active = await StartVersionAsync(_selection.ActiveVersion, cancellationToken);
                InstallActiveLocked(active, resetCrashCount: true);
                _lastError = null;
            }
            catch (Exception ex)
            {
                _active = null;
                _lastError = ex.Message;
            }

            return Status();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<EngineSupervisorStatus> ActivateAsync(string version, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateVersion(version);
        await _gate.WaitAsync(cancellationToken);
        EngineInstance? candidate = null;
        EngineInstance? previousInstance = null;
        try
        {
            candidate = await StartVersionAsync(version, cancellationToken);
            var next = new EngineSelectionState(
                version,
                string.Equals(_selection.ActiveVersion, version, StringComparison.Ordinal)
                    ? _selection.PreviousVersion
                    : _selection.ActiveVersion ?? _selection.PreviousVersion);

            PersistSelection(next);
            previousInstance = _active;
            InstallActiveLocked(candidate, resetCrashCount: true);
            candidate = null;
            _selection = next;
            _lastError = null;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            throw;
        }
        finally
        {
            if (candidate is not null)
                await candidate.DisposeAsync();
            _gate.Release();
        }

        if (previousInstance is not null)
            await previousInstance.DisposeAsync();
        return Status();
    }

    public async Task<EngineSupervisorStatus> RestartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        EngineInstance? candidate = null;
        EngineInstance? previousInstance = null;
        try
        {
            var version = _selection.ActiveVersion
                ?? throw new InvalidOperationException("No active engine version is configured.");
            candidate = await StartVersionAsync(version, cancellationToken);
            previousInstance = _active;
            InstallActiveLocked(candidate, resetCrashCount: true);
            candidate = null;
            _lastError = null;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            throw;
        }
        finally
        {
            if (candidate is not null)
                await candidate.DisposeAsync();
            _gate.Release();
        }

        if (previousInstance is not null)
            await previousInstance.DisposeAsync();
        return Status();
    }

    public Task<EngineSupervisorStatus> RollbackAsync(CancellationToken cancellationToken = default)
    {
        var previous = _selection.PreviousVersion
            ?? throw new InvalidOperationException("No previous engine version is available for rollback.");
        return ActivateAsync(previous, cancellationToken);
    }

    public string ResolveVersionExecutable(string version)
    {
        ValidateVersion(version);
        return Path.Combine(EngineRoot, version, "eye-engine.exe");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _gate.WaitAsync();
        try
        {
            ++_activeGeneration;
            var active = _active;
            _active = null;
            if (active is not null)
                await active.DisposeAsync();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private void InstallActiveLocked(EngineInstance instance, bool resetCrashCount)
    {
        _active = instance;
        _activeStartedAt = DateTimeOffset.UtcNow;
        if (resetCrashCount)
            _rapidCrashCount = 0;
        var generation = ++_activeGeneration;
        _ = MonitorActiveAsync(instance, generation);
    }

    private async Task MonitorActiveAsync(EngineInstance instance, long generation)
    {
        try
        {
            await instance.ExitTask;
        }
        catch
        {
        }

        if (Volatile.Read(ref _disposed) != 0)
            return;

        EngineInstance? replacement = null;
        try
        {
            await _gate.WaitAsync();
            try
            {
                if (Volatile.Read(ref _disposed) != 0 ||
                    generation != _activeGeneration ||
                    !ReferenceEquals(_active, instance))
                    return;

                var failedVersion = _selection.ActiveVersion;
                if (failedVersion is null)
                    return;

                var rapid = DateTimeOffset.UtcNow - _activeStartedAt <= CrashLoopWindow;
                _rapidCrashCount = rapid ? _rapidCrashCount + 1 : 1;
                _active = null;
                _lastError = $"Engine {failedVersion} exited unexpectedly.";

                if (_rapidCrashCount < 2)
                {
                    try
                    {
                        replacement = await StartVersionAsync(failedVersion, CancellationToken.None);
                        InstallActiveLocked(replacement, resetCrashCount: false);
                        replacement = null;
                        _lastError = $"Engine {failedVersion} exited unexpectedly and was restarted.";
                        return;
                    }
                    catch (Exception ex)
                    {
                        _lastError = $"Engine {failedVersion} restart failed: {ex.Message}";
                    }
                }

                var fallback = _selection.PreviousVersion;
                if (fallback is null)
                {
                    _lastError ??= $"Engine {failedVersion} entered a crash loop and no previous version is available.";
                    return;
                }

                try
                {
                    replacement = await StartVersionAsync(fallback, CancellationToken.None);
                    var next = new EngineSelectionState(fallback, failedVersion);
                    PersistSelection(next);
                    _selection = next;
                    InstallActiveLocked(replacement, resetCrashCount: true);
                    replacement = null;
                    _lastError = $"Engine {failedVersion} entered a crash loop; rolled back to {fallback}.";
                }
                catch (Exception ex)
                {
                    _lastError = $"Engine {failedVersion} failed and rollback to {fallback} also failed: {ex.Message}";
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            if (replacement is not null)
                await replacement.DisposeAsync();
            await instance.DisposeAsync();
        }
    }

    private Task<EngineInstance> StartVersionAsync(string version, CancellationToken cancellationToken) =>
        EngineInstance.StartAsync(ResolveVersionExecutable(version), _contract, TimeSpan.FromSeconds(10), cancellationToken);

    private EngineSelectionState LoadSelection()
    {
        if (!File.Exists(SelectorPath))
            return new EngineSelectionState(null, null);

        try
        {
            var state = JsonSerializer.Deserialize<EngineSelectionState>(File.ReadAllText(SelectorPath));
            if (state is null)
                throw new InvalidOperationException("Engine selector state is empty.");
            if (state.ActiveVersion is not null)
                ValidateVersion(state.ActiveVersion);
            if (state.PreviousVersion is not null)
                ValidateVersion(state.PreviousVersion);
            return state;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Invalid engine selector state: {ex.Message}", ex);
        }
    }

    private void PersistSelection(EngineSelectionState selection)
    {
        Directory.CreateDirectory(StateRoot);
        var temporary = SelectorPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(selection));
            File.Move(temporary, SelectorPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void ValidateVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version) ||
            version is "." or ".." ||
            version.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0 ||
            version.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Engine version must be a single valid directory name.", nameof(version));
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(EngineSupervisor));
    }
}