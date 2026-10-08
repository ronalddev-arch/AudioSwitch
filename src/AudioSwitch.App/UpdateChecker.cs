using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace AudioSwitch.App;

enum UpdateState { NotConfigured, NotInstalled, NotChecked, Checking, UpToDate, Available, Downloading, Failed }

/// <summary>
/// Looks for a newer release on GitHub (Velopack, RepositoryUrl in Directory.Build.props): shortly after startup, then
/// about once a day while <see cref="AutoCheck"/> is on. It never installs or restarts by itself: this app runs during
/// games and calls, so installing is always the user's choice (<see cref="InstallAsync"/>).
/// Create, use and dispose it on the UI thread; its events are raised there too.
/// </summary>
sealed class UpdateChecker : IDisposable
{
    static readonly TimeSpan FirstCheckAfter = TimeSpan.FromMinutes(1);
    static readonly TimeSpan CheckEvery = TimeSpan.FromHours(24);
    /// <summary>How often the schedule wakes up. Comparing wall-clock time each hour keeps the daily check close to
    /// schedule after the PC has slept, and retries a failed check (e.g. offline) an hour later.</summary>
    static readonly TimeSpan Tick = TimeSpan.FromHours(1);

    readonly ILogger _log;
    readonly SynchronizationContext _ui;
    readonly UpdateManager? _manager;
    readonly CancellationTokenSource _stop = new();
    UpdateInfo? _available;
    DateTime _lastCheckedUtc = DateTime.MinValue;
    string? _notifiedVersion;
    string? _error;

    public UpdateChecker(ILogger<UpdateChecker> log, bool autoCheck)
    {
        _log = log;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        AutoCheck = autoCheck;

        if (!IsInstalledBySetup()) State = UpdateState.NotInstalled;
        else if (AppInfo.RepositoryUrl is not { } repo) State = UpdateState.NotConfigured;
        else
        {
            // A pre-release build (1.0.0-beta.1) also takes newer pre-releases; a stable build only stable releases.
            _manager = new UpdateManager(new GithubSource(repo, accessToken: null, prerelease: AppInfo.Version.Contains('-')));
            State = UpdateState.NotChecked;
            _ = RunScheduleAsync();
        }
        _log.LogInformation("Updates: {Status}", StatusText);
    }

    public UpdateState State { get; private set; }

    /// <summary>The newer version found by the last check, or null.</summary>
    public string? AvailableVersion => _available?.TargetFullRelease.Version.ToString();

    public int DownloadPercent { get; private set; }

    /// <summary>The "Check for updates automatically" setting. Manual checks work either way.</summary>
    public bool AutoCheck { get; set; }

    public bool CanCheck => _manager is not null && State is not (UpdateState.Checking or UpdateState.Downloading);

    public bool CanInstall => _available is not null && State is not (UpdateState.Checking or UpdateState.Downloading);

    /// <summary>One line for the About window, the tray menu and the log.</summary>
    public string StatusText => State switch
    {
        UpdateState.NotInstalled => "Off: this copy wasn't installed with Setup.exe, so it can't update itself",
        UpdateState.NotConfigured => "Off: this build has no update source (RepositoryUrl)",
        UpdateState.NotChecked => AutoCheck ? "Not checked yet" : "Not checked (automatic checks are off)",
        UpdateState.Checking => "Checking…",
        UpdateState.UpToDate => $"Up to date (checked {_lastCheckedUtc.ToLocalTime():g})",
        UpdateState.Available => $"Version {AvailableVersion} is available",
        UpdateState.Downloading => $"Downloading version {AvailableVersion}… {DownloadPercent}%",
        UpdateState.Failed => $"Check failed: {_error}",
        _ => State.ToString(),
    };

    /// <summary>Raised on every <see cref="State"/> or progress change.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Raised once per new version found by an automatic check, for an unobtrusive notification.</summary>
    public event EventHandler<string>? UpdateFound;

    static bool IsInstalledBySetup()
    {
        try
        {
            var locator = VelopackLocator.Current;
            return locator.CurrentlyInstalledVersion is not null && !locator.IsPortable;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    async Task RunScheduleAsync()
    {
        try
        {
            await Task.Delay(FirstCheckAfter, _stop.Token);
            while (true)
            {
                if (AutoCheck && DateTime.UtcNow - _lastCheckedUtc >= CheckEvery) await CheckAsync(automatic: true);
                await Task.Delay(Tick, _stop.Token);
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task CheckAsync(bool automatic = false)
    {
        if (_manager is null || !CanCheck) return;
        SetState(UpdateState.Checking);
        try
        {
            var info = await Task.Run(_manager.CheckForUpdatesAsync, _stop.Token);
            if (_stop.IsCancellationRequested) return;
            _lastCheckedUtc = DateTime.UtcNow;
            _available = info;
            if (info is null)
            {
                _log.LogInformation("Update check: {Version} is the latest", AppInfo.Version);
                SetState(UpdateState.UpToDate);
                return;
            }
            var version = info.TargetFullRelease.Version.ToString();
            _log.LogInformation("Update check: version {Version} is available ({Kind} check)", version, automatic ? "automatic" : "manual");
            SetState(UpdateState.Available);
            if (_notifiedVersion == version) return;
            _notifiedVersion = version; // a manual check shows the result in the About window already
            if (automatic) UpdateFound?.Invoke(this, version);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _log.LogWarning("Update check failed: {Error}", ex.Message);
            _error = ex.Message;
            SetState(_available is not null ? UpdateState.Available : UpdateState.Failed); // keep offering a version found earlier
        }
    }

    /// <summary>Downloads the available update, hands it to Velopack's updater and calls <paramref name="exitApp"/>; the
    /// updater waits for this process to exit, applies the update and starts the new version. Returns an error message
    /// when it failed (the app keeps running).</summary>
    public async Task<string?> InstallAsync(Action exitApp)
    {
        if (_manager is null || _available is not { } info || !CanInstall) return null;
        DownloadPercent = 0;
        SetState(UpdateState.Downloading);
        try
        {
            _log.LogInformation("Downloading update {Version}", AvailableVersion);
            await Task.Run(() => _manager.DownloadUpdatesAsync(info, p => _ui.Post(_ =>
            {
                if (State != UpdateState.Downloading || p == DownloadPercent) return;
                DownloadPercent = p;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }, null), _stop.Token));
            if (_stop.IsCancellationRequested) return null;

            _log.LogInformation("Installing update {Version}; AudioSwitch restarts", AvailableVersion);
            _manager.WaitExitThenApplyUpdates(info.TargetFullRelease, silent: false, restart: true);
            exitApp();
            return null;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Installing update {Version} failed", AvailableVersion);
            SetState(UpdateState.Available);
            return ex.Message;
        }
    }

    void SetState(UpdateState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _stop.Cancel(); // not disposed: a late About-window click may still read its token
    }
}
