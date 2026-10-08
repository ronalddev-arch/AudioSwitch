using System.Threading.Channels;
using AudioSwitch.Core.Audio;
using AudioSwitch.Core.Sonar;
using Microsoft.Extensions.Logging;

namespace AudioSwitch.Core.Watching;

public sealed class AvailabilityChangedEventArgs(AvailabilitySnapshot previous, AvailabilitySnapshot current, IReadOnlyList<DeviceChange> changes) : EventArgs
{
    public AvailabilitySnapshot Previous { get; } = previous;
    public AvailabilitySnapshot Current { get; } = current;
    public IReadOnlyList<DeviceChange> Changes { get; } = changes;
}

/// <summary>
/// Merges Windows endpoint notifications and Sonar push events into one stream of availability changes. Any trigger
/// schedules a debounced re-evaluation that rebuilds the <see cref="AvailabilitySnapshot"/> and diffs it with the
/// previous one, which makes the result independent of event order and of devices flapping while (dis)connecting
/// (a Bluetooth device's endpoints come and go seconds apart).
/// </summary>
public sealed class AudioWatcher : IAsyncDisposable
{
    readonly IWindowsAudio _windows;
    readonly ISonarClient _sonar;
    readonly SonarEventStream _sonarEvents;
    readonly DeviceFilter _filter;
    readonly ILogger<AudioWatcher> _log;
    readonly TimeSpan _debounce;
    readonly Channel<string> _triggers = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    readonly CancellationTokenSource _stop = new();
    readonly SemaphoreSlim _evaluating = new(1, 1); // re-evaluation vs RefreshAsync: one snapshot owner at a time
    IReadOnlyDictionary<string, bool>? _lastWirelessStates;
    Task _running = Task.CompletedTask;

    public AudioWatcher(IWindowsAudio windows, ISonarClient sonar, SonarEventStream sonarEvents, DeviceFilter filter,
        ILogger<AudioWatcher> log, TimeSpan? debounce = null)
    {
        _windows = windows;
        _sonar = sonar;
        _sonarEvents = sonarEvents;
        _filter = filter;
        _log = log;
        _debounce = debounce ?? TimeSpan.FromSeconds(1.5);
    }

    public AvailabilitySnapshot Current { get; private set; } = AvailabilitySnapshot.Empty;

    /// <summary>Raised on a background thread after a re-evaluation found changes.</summary>
    public event EventHandler<AvailabilityChangedEventArgs>? AvailabilityChanged;

    /// <summary>Raised on a background thread when Sonar reports that its routing changed (by us, GG's UI or Sonar),
    /// and after (re)connecting to Sonar's event stream.</summary>
    public event EventHandler? SonarRoutingChanged;

    /// <summary>Takes the initial snapshot (raising no change events for what is already there) and starts watching.</summary>
    public async Task StartAsync(CancellationToken ct)
    {
        Current = await BuildSnapshotAsync(ct);
        _log.LogInformation("Usable outputs: {Outputs}", Describe(Current.Outputs));
        _log.LogInformation("Usable mics: {Mics}", Describe(Current.Mics));
        foreach (var x in Current.Excluded)
            _log.LogInformation("Ignoring {Endpoint}: {Reason}", x.Endpoint.Name, x.Reason);

        _windows.Changed += (_, description) => Trigger($"Windows: {description}");
        _sonarEvents.Connected += (_, _) =>
        {
            Trigger("Sonar event stream connected");
            SonarRoutingChanged?.Invoke(this, EventArgs.Empty); // Sonar may have restarted, possibly in another mode
        };
        _sonarEvents.EventReceived += OnSonarEvent;

        var token = _stop.Token;
        _running = Task.WhenAll(
            Task.Run(() => ProcessTriggersAsync(token), token),
            Task.Run(() => _sonarEvents.RunAsync(token), token));
    }

    void OnSonarEvent(object? sender, SonarEvent e)
    {
        switch (e.Name)
        {
            case SonarProtocol.EventFallbackUpdated:     // carries the headset on/off flag
            case SonarProtocol.EventDeviceStatusUpdate:  // an endpoint changed state
                Trigger($"Sonar: {e.Name}");
                break;
            case SonarProtocol.EventRedirectionStatusUpdate:
                SonarRoutingChanged?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    void Trigger(string reason) => _triggers.Writer.TryWrite(reason);

    async Task ProcessTriggersAsync(CancellationToken ct)
    {
        var reader = _triggers.Reader;
        var reasons = new List<string>();
        try
        {
            while (await reader.WaitToReadAsync(ct))
            {
                reasons.Clear();
                await Task.Delay(_debounce, ct); // let a burst of related events settle
                while (reader.TryRead(out var reason)) reasons.Add(reason);

                await _evaluating.WaitAsync(ct);
                try
                {
                    await ReevaluateAsync(reasons, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogError(ex, "Re-evaluating audio devices failed");
                }
                finally
                {
                    _evaluating.Release();
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    async Task ReevaluateAsync(IReadOnlyList<string> reasons, CancellationToken ct)
    {
        _log.LogDebug("Re-evaluating devices after: {Triggers}", string.Join("; ", reasons.Distinct()));
        var previous = Current;
        var next = await BuildSnapshotAsync(ct);
        Current = next;

        foreach (var x in next.Excluded.Where(x => !x.IsHeadsetOff && previous.Excluded.All(p => p.Endpoint.Id != x.Endpoint.Id)))
            _log.LogInformation("Ignoring {Endpoint}: {Reason}", x.Endpoint.Name, x.Reason);

        var changes = next.DiffFrom(previous);
        if (changes.Count == 0) return;
        foreach (var change in changes)
            _log.LogInformation("Device change: {Change}", change);
        AvailabilityChanged?.Invoke(this, new AvailabilityChangedEventArgs(previous, next, changes));
    }

    /// <summary>
    /// Rebuilds the snapshot after the <see cref="DeviceFilter"/> changed (settings edited), without raising
    /// <see cref="AvailabilityChanged"/>: editing the exclusion list must not count as devices arriving or leaving,
    /// which could prompt, switch or disconnect.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        await _evaluating.WaitAsync(ct);
        try
        {
            Current = await BuildSnapshotAsync(ct);
            _log.LogInformation("Settings changed; usable outputs: {Outputs}; usable mics: {Mics}",
                Describe(Current.Outputs), Describe(Current.Mics));
        }
        finally
        {
            _evaluating.Release();
        }
    }

    async Task<AvailabilitySnapshot> BuildSnapshotAsync(CancellationToken ct)
    {
        var endpoints = _windows.GetActiveEndpoints();
        try
        {
            _lastWirelessStates = await _sonar.GetWirelessStatesAsync(ct);
        }
        catch (Exception ex) when (ex is SonarUnavailableException or SonarApiException)
        {
            // Keep the last known headset state rather than flip-flopping while GG restarts.
            _log.LogDebug("Headset state unavailable ({Error}); using last known state", ex.Message);
        }
        return AvailabilitySnapshot.Build(endpoints, _lastWirelessStates, _filter) with
        {
            DefaultOutputId = TryRead(_windows.GetDefaultOutputId, "output"),
            DefaultMicId = TryRead(_windows.GetDefaultInputId, "recording device"),
        };
    }

    string? TryRead(Func<string?> read, string what)
    {
        try { return read(); }
        catch (Exception ex)
        {
            _log.LogDebug("Reading the Windows default {What} failed: {Error}", what, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Entering Windows-only mode: the last headset on/off state Sonar reported can no longer be updated, so forget it
    /// (a SteelSeries headset then counts as usable, like any device). Rebuilds the snapshot without raising events.
    /// </summary>
    public async Task ForgetHeadsetStatesAsync(CancellationToken ct)
    {
        await _evaluating.WaitAsync(ct);
        try
        {
            if (_lastWirelessStates is null) return;
            _lastWirelessStates = null;
            Current = await BuildSnapshotAsync(ct);
            _log.LogInformation("Headset states forgotten; usable outputs: {Outputs}", Describe(Current.Outputs));
        }
        finally
        {
            _evaluating.Release();
        }
    }

    static string Describe(IEnumerable<AudioEndpoint> endpoints) =>
        string.Join(", ", endpoints.Select(e => e.Name)) is { Length: > 0 } s ? s : "(none)";

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _triggers.Writer.TryComplete();
        try { await _running; } catch (OperationCanceledException) { }
        _stop.Dispose();
        _evaluating.Dispose();
    }
}
