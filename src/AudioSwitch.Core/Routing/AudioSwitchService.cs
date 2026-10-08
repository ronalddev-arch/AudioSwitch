using AudioSwitch.Core.Audio;
using AudioSwitch.Core.Settings;
using AudioSwitch.Core.Sonar;
using AudioSwitch.Core.Watching;
using Microsoft.Extensions.Logging;

namespace AudioSwitch.Core.Routing;

/// <summary>A pending "switch to this device?" question for the UI.</summary>
public sealed record PromptRequest(PromptDecision Decision, IReadOnlyList<AudioEndpoint> AvailableMics);

/// <summary>What the tray shows. In Windows-only mode <see cref="OutputName"/>/<see cref="MicName"/> are the Windows
/// default playback and recording devices.</summary>
public sealed record RoutingStatus(bool SonarAvailable, string? OutputName, string? MicName, IReadOnlyList<AudioEndpoint> Outputs)
{
    /// <summary>What AudioSwitch manages right now.</summary>
    public SwitchingMode Mode { get; init; }

    /// <summary>Why, short, e.g. "GG not installed".</summary>
    public string ModeReason { get; init; } = "";

    public bool WindowsOnly => Mode == SwitchingMode.WindowsOnly;
}

/// <summary>
/// Glue between the watcher, the rules and the executor. Handles one availability change at a time, applies automatic
/// fallbacks, and hands questions to the UI via <see cref="PromptRequested"/>. All events are raised on background threads.
/// </summary>
public sealed class AudioSwitchService(
    AudioWatcher watcher,
    IWindowsAudio windows,
    ISonarClient sonar,
    RoutingRules rules,
    RoutingEngine engine,
    RoutingExecutor executor,
    RecentOutputs recent,
    ILogger<AudioSwitchService> log)
{
    // Also polls Sonar's mode, which no WebSocket event is known to announce.
    static readonly TimeSpan ModeCheckInterval = TimeSpan.FromSeconds(5);

    readonly SemaphoreSlim _gate = new(1, 1);
    readonly SwitchingModeRule _modeRule = new(SwitchingModeRule.DefaultGrace);
    CancellationToken _ct;
    SonarMode? _lastMode; // the mode the last mode check saw; null until Sonar was read
    bool _windowsOnlyBeforeSonar; // Windows-only mode was used since Sonar was last seen: repair Sonar when it returns

    public event EventHandler<PromptRequest>? PromptRequested;

    /// <summary>The device went away while its prompt was pending: endpoint id.</summary>
    public event EventHandler<string>? PromptCancelled;

    /// <summary>Something worth a tray notification happened (title, text).</summary>
    public event EventHandler<(string Title, string Text)>? Notification;

    public event EventHandler? StatusChanged;

    public async Task StartAsync(CancellationToken ct)
    {
        _ct = ct;
        watcher.AvailabilityChanged += (_, e) => _ = HandleAsync(e);
        watcher.SonarRoutingChanged += (_, _) => _ = CheckModesAsync(alwaysNotify: true);
        await watcher.StartAsync(ct);

        var probe = await ProbeSonarAsync();
        await RunExclusiveAsync(async () =>
        {
            await ApplyProbeAsync(probe);
            var current = await ReadCurrentRoutingAsync();
            foreach (var decision in engine.DecideAtStartup(watcher.Current, current, recent.Keys))
                await ExecuteAsync(decision);
        });
        StatusChanged?.Invoke(this, EventArgs.Empty);
        _ = WatchModesAsync();
    }

    async Task WatchModesAsync()
    {
        while (!_ct.IsCancellationRequested)
        {
            try { await Task.Delay(ModeCheckInterval, _ct); }
            catch (OperationCanceledException) { return; }
            await CheckModesAsync(alwaysNotify: false);
        }
    }

    /// <summary>
    /// Probes Sonar and updates the <see cref="SwitchingMode"/> and Sonar's classic/stream mode. Runs every
    /// <see cref="ModeCheckInterval"/>, and whenever Sonar's routing changed (by us, GG's UI, Sonar itself, or Sonar
    /// reconnected).
    /// </summary>
    async Task CheckModesAsync(bool alwaysNotify)
    {
        try
        {
            var probe = await ProbeSonarAsync(); // outside the gate: GG may take seconds to answer
            var changed = false;
            await RunExclusiveAsync(async () => changed = await ApplyProbeAsync(probe));
            if (changed || alwaysNotify) StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { }
    }

    async Task<(SonarPresence Presence, SonarMode? Mode)> ProbeSonarAsync()
    {
        try { return (SonarPresence.Running, await sonar.GetModeAsync(_ct)); }
        catch (SonarUnavailableException ex) { return (ex.Presence, null); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogDebug("Sonar answered the mode check with an error: {Error}", ex.Message);
            return (SonarPresence.Running, null); // it answered, so it runs
        }
    }

    /// <summary>
    /// Feeds a probe to the <see cref="SwitchingModeRule"/> and reacts to what changed (call inside the gate):
    /// <list type="bullet">
    /// <item>Windows-only mode starts: forget the headset states Sonar can no longer update.</item>
    /// <item>Sonar is back after Windows-only mode: its routing may point at devices that left meanwhile; repair it.</item>
    /// <item>Sonar switched between classic and stream mode: the new mode has its own devices; repair them.</item>
    /// </list>
    /// Returns true when the tray should refresh.
    /// </summary>
    async Task<bool> ApplyProbeAsync((SonarPresence Presence, SonarMode? Mode) probe)
    {
        var previous = _modeRule.Current;
        var verdict = _modeRule.Next(probe.Presence, DateTimeOffset.UtcNow);
        var modeChanged = verdict.Mode != previous.Mode;
        var changed = verdict != previous; // a new reason is worth a log line and a tray refresh too
        if (changed)
            log.LogInformation("{What}: {Reason}", verdict.Mode switch
            {
                SwitchingMode.WindowsOnly => "Windows-only mode, switching just the Windows default devices",
                SwitchingMode.Sonar => "Sonar mode, switching Sonar and the Windows default",
                _ => "Not switching anything until it is clear whether Sonar is in use",
            }, verdict.Reason);

        if (verdict.Mode == SwitchingMode.WindowsOnly)
        {
            if (modeChanged)
            {
                _windowsOnlyBeforeSonar = true;
                await watcher.ForgetHeadsetStatesAsync(_ct);
            }
            return changed;
        }
        if (verdict.Mode != SwitchingMode.Sonar || probe.Mode is not { } mode) return changed;

        var previousMode = _lastMode;
        _lastMode = mode;
        if (_windowsOnlyBeforeSonar)
        {
            _windowsOnlyBeforeSonar = false;
            await RepairAsync("when Sonar came back");
            return true;
        }
        if (previousMode is null || previousMode == mode) return changed;

        log.LogInformation("Sonar switched from {Previous} to {Mode} mode", previousMode.Value.ToString(), mode.ToString());
        await RepairAsync(when: null);
        return true;
    }

    async Task RepairAsync(string? when)
    {
        var current = await ReadCurrentRoutingAsync();
        foreach (var decision in engine.DecideAfterModeChange(watcher.Current, current, recent.Keys, when))
            await ExecuteAsync(decision);
    }

    async Task HandleAsync(AvailabilityChangedEventArgs e)
    {
        foreach (var gone in e.Changes.Where(c => c.Kind == DeviceChangeKind.Departed))
            PromptCancelled?.Invoke(this, gone.Endpoint.Id);

        await RunExclusiveAsync(async () =>
        {
            var current = await ReadCurrentRoutingAsync(before: e.Previous);
            foreach (var decision in engine.Decide(e.Changes, e.Current, current, recent.Keys))
                await ExecuteAsync(decision);
        });
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    async Task ExecuteAsync(RoutingDecision decision)
    {
        switch (decision)
        {
            case NoActionDecision:
                log.LogInformation("No action: {Reason}", decision.Reason);
                break;
            case AutoSwitchDecision auto:
                log.LogInformation("Automatic switch: {Reason}", auto.Reason);
                await ApplyAndRememberAsync(auto.Plan, acknowledgeCause: auto.Cause);
                break;
            case PromptDecision prompt:
                log.LogInformation("Asking: {Plan}? Why: {Reason}", prompt.Plan, prompt.Reason);
                PromptRequested?.Invoke(this, new PromptRequest(prompt, watcher.Current.Mics));
                break;
            case DisconnectDecision disconnect:
                log.LogInformation("Disconnecting: {Reason}", disconnect.Reason);
                Disconnect(disconnect.Device, disconnect.DisplayName);
                break;
        }
    }

    void Disconnect(AudioEndpoint device, string name)
    {
        try
        {
            executor.DisconnectBluetooth(device);
            Notification?.Invoke(this, ("Disconnected", $"{name} was disconnected from this PC."));
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Disconnecting {Device} failed", name);
            Notification?.Invoke(this, ("Disconnect failed", $"Could not disconnect {name}: {ex.Message}"));
        }
    }

    /// <summary>The user accepted a prompt, possibly after editing the plan.</summary>
    public Task AcceptAsync(PromptRequest request, SwitchPlan plan) => RunExclusiveAsync(async () =>
    {
        if (!watcher.Current.IsUsable(request.Decision.Device.Id))
        {
            log.LogInformation("Accepted switch to {Device}, but it is no longer available", request.Decision.DisplayName);
            return;
        }
        log.LogInformation("User accepted switch to {Device}", request.Decision.DisplayName);
        if (plan.WindowsOnly != (_modeRule.Current.Mode == SwitchingMode.WindowsOnly))
        {
            // Sonar came or went while the popup was open: plan again for the new situation, keeping the user's choices.
            var fresh = rules.PlanSwitchTo(request.Decision.Device, watcher.Current, plan.Reason, await ReadCurrentRoutingAsync());
            plan = fresh with { WindowsDefaults = plan.WindowsDefaults is null ? null : fresh.WindowsDefaults, Mic = plan.Mic };
            log.LogInformation("{Mode} since the popup appeared; switching with: {Plan}",
                fresh.WindowsOnly ? "Windows-only mode" : "Sonar mode", plan);
        }
        await ApplyAndRememberAsync(plan, acknowledgeCause: null);
    }, notifyStatus: true);

    /// <summary>The user declined a prompt, or it timed out.</summary>
    public Task DeclineAsync(PromptRequest request, bool timedOut) => RunExclusiveAsync(() =>
    {
        var name = request.Decision.DisplayName;
        log.LogInformation("Switch to {Device} {Outcome}; routing unchanged", name, timedOut ? "timed out" : "declined");
        if (request.Decision.DisconnectWhenDeclined) Disconnect(request.Decision.Device, name);
        return Task.CompletedTask;
    }, notifyStatus: true);

    /// <summary>Manual switch from the tray menu.</summary>
    public Task SwitchToAsync(AudioEndpoint output) => RunExclusiveAsync(async () =>
    {
        var current = await ReadCurrentRoutingAsync();
        var plan = rules.PlanSwitchTo(output, watcher.Current, $"{rules.DisplayName(output)} chosen in the tray menu", current);
        await ApplyAndRememberAsync(plan, acknowledgeCause: null);
    }, notifyStatus: true);

    public async Task<RoutingStatus> GetStatusAsync()
    {
        var snapshot = watcher.Current;
        var switching = _modeRule.Current;
        if (switching.Mode == SwitchingMode.WindowsOnly)
        {
            // Any active endpoint: the Windows default may be an ignored device the user picked.
            string? WindowsName(string? id) => id is null ? null : snapshot.Active.FirstOrDefault(e => e.Id == id)?.Name ?? "(unknown device)";
            return new RoutingStatus(false, WindowsName(TryReadWindows(windows.GetDefaultOutputId, "output")),
                WindowsName(TryReadWindows(windows.GetDefaultInputId, "recording device")), snapshot.Outputs)
            { Mode = switching.Mode, ModeReason = switching.Reason };
        }
        try
        {
            var routing = await sonar.GetRoutingAsync(_ct);
            string? NameOf(string? id) => id is null ? null : snapshot.Usable.FirstOrDefault(e => e.Id == id)?.Name ?? "(unavailable device)";
            return new RoutingStatus(true, NameOf(routing.Output), NameOf(routing.Mic), snapshot.Outputs)
            { Mode = switching.Mode, ModeReason = switching.Reason };
        }
        catch (Exception ex) when (ex is SonarUnavailableException or SonarApiException)
        {
            return new RoutingStatus(false, null, null, snapshot.Outputs) { Mode = switching.Mode, ModeReason = switching.Reason };
        }
    }

    /// <param name="acknowledgeCause">For automatic switches: why it happened, shown in a notification. Null for
    /// switches the user just chose, which only notify when something failed.</param>
    async Task ApplyAndRememberAsync(SwitchPlan plan, string? acknowledgeCause)
    {
        var failures = await executor.ApplyAsync(plan, _ct);
        if (plan.Output is { } output) recent.Touch(rules.RecentKey(output));
        if (acknowledgeCause is null && failures.Count == 0) return;

        var title = plan.Output is { } o ? $"Switched to {rules.DisplayName(o)}"
            : plan.Mic is { } m ? $"Mic switched to {m.DisplayDeviceName}"
            : "Audio switched";
        var lines = new List<string>();
        if (acknowledgeCause is not null) lines.Add(char.ToUpper(acknowledgeCause[0]) + acknowledgeCause[1..]);
        if (plan is { Output: not null, Mic: { } mic }) lines.Add($"Mic → {mic.DisplayDeviceName}");
        if (failures.Count > 0) lines.Add($"Problems: {string.Join("; ", failures)}");
        Notification?.Invoke(this, (failures.Count > 0 ? $"{title} (with problems)" : title, string.Join("\n", lines)));
    }

    /// <param name="before">The snapshot from before the change being handled; in Windows-only mode its defaults tell
    /// what was in use, since Windows moves a default by itself when its device goes away.</param>
    async Task<CurrentRouting> ReadCurrentRoutingAsync(AvailabilitySnapshot? before = null)
    {
        var windowsDefault = TryReadWindows(windows.GetDefaultOutputId, "output");
        if (_modeRule.Current.Mode == SwitchingMode.WindowsOnly)
            return CurrentRouting.FromWindows(windowsDefault, TryReadWindows(windows.GetDefaultInputId, "recording device"), before);

        try
        {
            var routing = await sonar.GetRoutingAsync(_ct);
            if (routing.Mode == SonarMode.Unknown)
                log.LogWarning("Sonar is in an unknown mode '{Mode}'; managing it like classic mode", routing.RawMode);
            return new CurrentRouting(routing.Devices, windowsDefault, Mode: routing.Mode);
        }
        catch (Exception ex) when (ex is SonarUnavailableException or SonarApiException)
        {
            log.LogWarning("Reading Sonar routing failed: {Error}", ex.Message);
            return CurrentRouting.Unknown with { WindowsDefaultOutput = windowsDefault };
        }
    }

    string? TryReadWindows(Func<string?> read, string what)
    {
        try { return read(); }
        catch (Exception ex)
        {
            log.LogWarning("Reading the Windows default {What} failed: {Error}", what, ex.Message);
            return null;
        }
    }

    async Task RunExclusiveAsync(Func<Task> action, bool notifyStatus = false)
    {
        await _gate.WaitAsync(_ct);
        try { await action(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Routing failed");
        }
        finally { _gate.Release(); }
        if (notifyStatus) StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
