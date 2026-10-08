using AudioSwitch.Core.Audio;
using AudioSwitch.Core.Routing;
using AudioSwitch.Core.Settings;
using AudioSwitch.Core.Sonar;
using AudioSwitch.Core.Watching;
using Microsoft.Extensions.Logging;

namespace AudioSwitch.App;

sealed class TrayApplicationContext : ApplicationContext
{
    readonly ILogger _log;
    readonly NotifyIcon _tray;
    readonly Icon _activeIcon = TrayIcons.Create(TrayIcons.Active);
    readonly Icon _inactiveIcon = TrayIcons.Create(TrayIcons.Inactive);
    readonly SynchronizationContext _ui;
    readonly CancellationTokenSource _shutdown = new();
    readonly string _settingsPath, _logDir;
    AudioSwitchSettings _settings;
    readonly DeviceFilter _filter;
    readonly WindowsAudio _windowsAudio;
    readonly SonarDiscovery _discovery;
    readonly SonarClient _sonar;
    readonly AudioWatcher _watcher;
    readonly RoutingRules _rules;
    readonly AudioSwitchService _service;
    readonly UpdateChecker _updates;
    readonly Queue<PromptRequest> _pendingPrompts = new();
    SwitchPromptForm? _activePrompt;
    SettingsForm? _settingsForm;
    AboutForm? _aboutForm;
    FirstRunForm? _firstRunForm;
    RoutingStatus? _status;
    bool _exiting;

    readonly RegisteredWaitHandle _showRequestedWait;

    /// <param name="showRequested">Set by a second start of the app (see Program): answered with a toast.</param>
    public TrayApplicationContext(ILoggerFactory loggers, string dataDir, string logDir, WaitHandle showRequested)
    {
        _log = loggers.CreateLogger<TrayApplicationContext>();
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _logDir = logDir;

        _log.LogInformation("AudioSwitch {Version} starting", AppInfo.FullVersion);

        _settingsPath = Path.Combine(dataDir, "settings.json");
        // A fresh install: no settings.json yet. It is written when the first-run setup closes (finished or not), so
        // the setup comes back only if the app never got that far.
        var firstRun = !File.Exists(_settingsPath);
        var loaded = firstRun ? AudioSwitchSettings.CreateDefault() : JsonFile.LoadOrCreate(_settingsPath, AudioSwitchSettings.CreateDefault(), _log);
        _settings = loaded.Migrate();
        if (!ReferenceEquals(_settings, loaded))
        {
            JsonFile.Save(_settingsPath, _settings);
            _log.LogInformation("Upgraded {Path} from schema v{Old} to v{New}", _settingsPath, loaded.SchemaVersion, _settings.SchemaVersion);
        }
        var statePath = Path.Combine(dataDir, "state.json");
        var state = JsonFile.LoadOrCreate(statePath, new AppState(), _log);
        var recent = new RecentOutputs(state.RecentOutputs, keys => SaveState(statePath, keys));

        _windowsAudio = new WindowsAudio();
        _discovery = new SonarDiscovery(loggers.CreateLogger<SonarDiscovery>());
        _sonar = new SonarClient(_discovery, loggers.CreateLogger<SonarClient>());
        _filter = new DeviceFilter(_settings.ToFilterOptions());
        _watcher = new AudioWatcher(_windowsAudio, _sonar, new SonarEventStream(_discovery, loggers.CreateLogger<SonarEventStream>()),
            _filter, loggers.CreateLogger<AudioWatcher>());
        _rules = new RoutingRules(_settings);
        _service = new AudioSwitchService(_watcher, _windowsAudio, _sonar, _rules, new RoutingEngine(_rules),
            new RoutingExecutor(_windowsAudio, _sonar, loggers.CreateLogger<RoutingExecutor>()), recent,
            loggers.CreateLogger<AudioSwitchService>());

        var menu = new ContextMenuStrip();
        menu.Opening += (_, e) =>
        {
            BuildMenu(menu);
            e.Cancel = false; // WinForms pre-cancels opening an empty menu, so the first right-click showed nothing
        };
        _tray = new NotifyIcon
        {
            Icon = _inactiveIcon,
            Text = "AudioSwitch (starting…)",
            ContextMenuStrip = menu,
            Visible = true,
        };

        _service.PromptRequested += (_, request) => _ui.Post(_ => EnqueuePrompt(request), null);
        _service.PromptCancelled += (_, endpointId) => _ui.Post(_ => CancelPrompts(endpointId), null);
        _service.Notification += (_, n) => _ui.Post(_ => ShowToast(n.Title, n.Text), null);
        _service.StatusChanged += (_, _) => _ = RefreshStatusAsync();

        _updates = new UpdateChecker(loggers.CreateLogger<UpdateChecker>(), _settings.CheckForUpdatesAutomatically);
        _updates.UpdateFound += (_, version) => ShowToast($"{AppInfo.Product} {version} is available",
            "Install it from the tray menu whenever it suits you. AudioSwitch restarts to install; nothing happens until you choose to.");

        CheckSteelSeriesVersions();
        _showRequestedWait = ThreadPool.RegisterWaitForSingleObject(showRequested, (_, _) => _ui.Post(_ =>
        {
            _log.LogInformation("Started again while already running");
            ShowToast($"{AppInfo.Product} is already running", "Right-click its icon in the tray, next to the clock, for the menu and settings.");
        }, null), null, Timeout.Infinite, executeOnlyOnce: false);
        _ = StartAsync();
        if (firstRun)
        {
            _log.LogInformation("No {Path} yet: first run, showing the setup", _settingsPath);
            _ui.Post(_ => ShowFirstRun(), null);
        }
    }

    async Task StartAsync()
    {
        try
        {
            await _service.StartAsync(_shutdown.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Starting AudioSwitch failed");
            _tray.Text = "AudioSwitch (failed to start — see log)";
        }
    }

    /// <summary>Sonar's API is unofficial, so say so when GG has been updated past the version this build was verified with.</summary>
    void CheckSteelSeriesVersions()
    {
        var verified = AppInfo.VerifiedSteelSeries;
        var installed = SteelSeriesVersions.DetectInstalled();
        _log.LogInformation("Verified with {Verified}; installed {Installed}", verified, installed);
        if (!installed.DiffersFrom(verified)) return;

        _log.LogWarning("Installed SteelSeries versions differ from the verified ones; Sonar's unofficial API may have changed");
        _tray.ShowBalloonTip(8000, "AudioSwitch: GG was updated",
            $"Installed: {installed}\nVerified: {verified}\nIf switching stops working, the Sonar API may have changed.",
            ToolTipIcon.Warning);
    }

    // --- Prompts: one at a time, newest device questions queue behind the visible one.

    void EnqueuePrompt(PromptRequest request)
    {
        var deviceId = request.Decision.Device.Id;
        if (_activePrompt?.Request.Decision.Device.Id == deviceId || _pendingPrompts.Any(p => p.Decision.Device.Id == deviceId))
            return; // already asking about this device
        _pendingPrompts.Enqueue(request);
        ShowNextPrompt();
    }

    void ShowNextPrompt()
    {
        if (_exiting || _activePrompt is not null || !_pendingPrompts.TryDequeue(out var request)) return;
        var form = new SwitchPromptForm(request, _settings.PopupTimeoutSeconds, _activeIcon);
        form.FormClosed += (_, _) => OnPromptClosed(form);
        _activePrompt = form;
        form.Show();
    }

    void OnPromptClosed(SwitchPromptForm form)
    {
        _activePrompt = null;
        if (!form.Cancelled && !_exiting)
        {
            if (form.AcceptedPlan is { } plan) _ = _service.AcceptAsync(form.Request, plan);
            else _ = _service.DeclineAsync(form.Request, form.TimedOut);
        }
        form.Dispose();
        ShowNextPrompt();
    }

    void CancelPrompts(string endpointId)
    {
        var keep = _pendingPrompts.Where(p => p.Decision.Device.Id != endpointId).ToList();
        _pendingPrompts.Clear();
        foreach (var p in keep) _pendingPrompts.Enqueue(p);

        if (_activePrompt?.Request.Decision.Device.Id == endpointId)
        {
            _log.LogInformation("{Device} went away; closing its prompt", _activePrompt.Request.Decision.DisplayName);
            _activePrompt.CancelPrompt();
        }
    }

    // --- Toasts: one at a time (a newer one replaces the old), stacked above an open switch prompt.

    ToastForm? _toast;

    void ShowToast(string title, string text)
    {
        if (_exiting) return;
        _log.LogDebug("Toast: {Title} — {Text}", title, text.ReplaceLineEndings(" · "));
        _toast?.Close();
        var offset = _activePrompt is { Visible: true } prompt ? prompt.Height + 8 : 0;
        var toast = new ToastForm(title, text, _activeIcon, offset);
        toast.FormClosed += (_, _) =>
        {
            if (_toast == toast) _toast = null;
            toast.Dispose();
        };
        _toast = toast;
        toast.Show();
    }

    // --- Tray

    async Task RefreshStatusAsync()
    {
        var status = await _service.GetStatusAsync();
        _ui.Post(_ =>
        {
            _status = status;
            // Windows-only mode is working as intended (no Sonar to manage), so it gets the normal icon, not the grey one.
            _tray.Icon = status.SonarAvailable || status.WindowsOnly ? _activeIcon : _inactiveIcon;
            var text = status.SonarAvailable || status.WindowsOnly
                ? $"AudioSwitch{(status.WindowsOnly ? ": Windows only" : "")}\nAudio: {status.OutputName ?? "?"}\nMic: {status.MicName ?? "?"}"
                : "AudioSwitch\nGG/Sonar not reachable";
            _tray.Text = text.Length <= 127 ? text : text[..124] + "…"; // NotifyIcon.Text limit
            _firstRunForm?.ShowStatus(status);
        }, null);
    }

    void BuildMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();
        if (_status is { WindowsOnly: true } w)
            menu.Items.Add(new ToolStripMenuItem($"Windows only ({w.ModeReason})") { Enabled = false });
        if (_status is { SonarAvailable: true } or { WindowsOnly: true })
        {
            var s = _status;
            menu.Items.Add(new ToolStripMenuItem($"Audio: {s.OutputName ?? "?"}") { Enabled = false });
            menu.Items.Add(new ToolStripMenuItem($"Mic: {s.MicName ?? "?"}") { Enabled = false });
        }
        else
        {
            menu.Items.Add(new ToolStripMenuItem("GG/Sonar not reachable") { Enabled = false });
        }
        menu.Items.Add(new ToolStripSeparator());

        var switchTo = new ToolStripMenuItem("Switch audio to");
        foreach (var output in _watcher.Current.Outputs)
        {
            var item = new ToolStripMenuItem(_rules.DisplayName(output)) { Checked = output.Name == _status?.OutputName };
            item.Click += (_, _) => _ = _service.SwitchToAsync(output);
            switchTo.DropDownItems.Add(item);
        }
        switchTo.Enabled = switchTo.DropDownItems.Count > 0;
        menu.Items.Add(switchTo);
        menu.Items.Add(new ToolStripSeparator());

        if (_updates.State == UpdateState.Downloading)
        {
            menu.Items.Add(new ToolStripMenuItem($"Downloading update… {_updates.DownloadPercent}%") { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
        }
        else if (_updates.CanInstall)
        {
            menu.Items.Add($"Install update {_updates.AvailableVersion} (restarts)", null, (_, _) => _ = InstallUpdateAsync());
            menu.Items.Add(new ToolStripSeparator());
        }

        // Start with Windows lives on Settings → General only, so there is one place to change it.
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add("About…", null, (_, _) => ShowAbout());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
    }

    // --- Settings and About: one window each; choosing it again brings it to the front.

    void ShowSettings()
    {
        if (BringToFront(_firstRunForm) || BringToFront(_settingsForm)) return; // the setup writes the settings itself
        IReadOnlyList<AudioEndpoint> activeEndpoints = [];
        try { activeEndpoints = _windowsAudio.GetActiveEndpoints(); }
        catch (Exception ex) { _log.LogWarning("Listing audio devices for the settings window failed: {Error}", ex.Message); }

        _settingsForm = new SettingsForm(_settings, ApplySettings, _logDir, activeEndpoints, () => _status?.Mode, _activeIcon);
        _settingsForm.FormClosed += (_, _) => { _settingsForm.Dispose(); _settingsForm = null; };
        _settingsForm.Show();
    }

    void ShowAbout()
    {
        if (BringToFront(_aboutForm)) return;
        _aboutForm = new AboutForm(_activeIcon, _updates, () => _ = InstallUpdateAsync());
        _aboutForm.FormClosed += (_, _) => { _aboutForm.Dispose(); _aboutForm = null; };
        _aboutForm.Show();
    }

    // --- First-run setup: once, when settings.json did not exist at startup.

    void ShowFirstRun()
    {
        if (_exiting) return;
        IReadOnlyList<AudioEndpoint> activeEndpoints = [];
        try { activeEndpoints = _windowsAudio.GetActiveEndpoints(); }
        catch (Exception ex) { _log.LogWarning("Listing audio devices for the first-run setup failed: {Error}", ex.Message); }

        _firstRunForm = new FirstRunForm(_settings, ApplySettings, activeEndpoints, _status, _activeIcon);
        _firstRunForm.FormClosed += (_, _) => OnFirstRunClosed();
        _firstRunForm.Show();
        _firstRunForm.Activate();
    }

    void OnFirstRunClosed()
    {
        var finished = _firstRunForm?.Finished == true;
        _firstRunForm?.Dispose();
        _firstRunForm = null;
        if (finished)
            _log.LogInformation("First-run setup finished");
        else
        {
            // Closed, cancelled or the app exits: keep the defaults, so that the setup doesn't come back.
            try
            {
                JsonFile.Save(_settingsPath, _settings);
                _log.LogInformation("First-run setup closed without finishing; saved the default settings to {Path}", _settingsPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.LogError(ex, "Saving {Path} failed", _settingsPath);
            }
        }
        if (!_exiting)
            ShowToast($"{AppInfo.Product} is running",
                "It lives in the tray next to the clock (look under ^ if you don't see it). Right-click its icon for Settings.");
    }

    /// <summary>Only ever on the user's click: downloads, then exits so that Velopack's updater can install and restart.</summary>
    async Task InstallUpdateAsync()
    {
        if (await _updates.InstallAsync(ExitThread) is { } error)
            ShowToast("Update not installed", error);
    }

    static bool BringToFront(Form? form)
    {
        if (form is null) return false;
        if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
        form.Activate();
        return true;
    }

    /// <summary>Saves the settings window's result and applies it right away. Returns an error message on failure.</summary>
    string? ApplySettings(AudioSwitchSettings settings)
    {
        try
        {
            JsonFile.Save(_settingsPath, settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "Saving {Path} failed", _settingsPath);
            return $"Could not save {_settingsPath}: {ex.Message}";
        }

        _settings = settings;
        _rules.Update(settings);
        _updates.AutoCheck = settings.CheckForUpdatesAutomatically;
        _filter.Update(settings.ToFilterOptions());
        _log.LogInformation("Settings saved: devices [{Profiles}], popup {Timeout}s, ignoring [{Excluded}], automatic update checks {UpdateChecks}",
            string.Join(", ", settings.Profiles.Select(p => $"{p.Name} ~ '{p.Match}'")), settings.PopupTimeoutSeconds,
            string.Join(", ", settings.ExcludedNameFragments), settings.CheckForUpdatesAutomatically ? "on" : "off");
        _ = RefreshAfterSettingsChangedAsync();
        return null;
    }

    async Task RefreshAfterSettingsChangedAsync()
    {
        try
        {
            await _watcher.RefreshAsync(_shutdown.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Re-reading devices after a settings change failed");
        }
        await RefreshStatusAsync();
    }

    void SaveState(string path, IReadOnlyList<string> recentOutputs)
    {
        try { JsonFile.Save(path, new AppState { RecentOutputs = recentOutputs }); }
        catch (Exception ex) { _log.LogWarning("Saving {Path} failed: {Error}", path, ex.Message); }
    }

    protected override void ExitThreadCore()
    {
        _log.LogInformation("AudioSwitch exiting");
        _exiting = true; // closing an open prompt must not count as "declined" (which could disconnect a device)
        _activePrompt?.Close();
        _toast?.Close();
        _settingsForm?.CloseWithoutAsking();
        _aboutForm?.Close();
        _firstRunForm?.Close(); // saves the defaults (OnFirstRunClosed)
        _tray.Visible = false;
        _shutdown.Cancel();
        _updates.Dispose();
        _showRequestedWait.Unregister(null);
        // Off the UI thread so its continuations can't deadlock on us; the process is exiting anyway.
        Task.Run(() => _watcher.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(2));
        _windowsAudio.Dispose();
        _sonar.Dispose();
        _discovery.Dispose();
        _tray.Dispose();
        _activeIcon.Dispose();
        _inactiveIcon.Dispose();
        base.ExitThreadCore();
    }
}
