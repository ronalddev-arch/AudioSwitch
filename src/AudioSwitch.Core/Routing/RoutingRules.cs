using AudioSwitch.Core.Audio;
using AudioSwitch.Core.Settings;
using AudioSwitch.Core.Sonar;
using AudioSwitch.Core.Watching;

namespace AudioSwitch.Core.Routing;

/// <summary>The user's preferences from <see cref="AudioSwitchSettings"/>: device profiles, mic preference lists and
/// fallback order.</summary>
public sealed class RoutingRules(AudioSwitchSettings settings)
{
    volatile AudioSwitchSettings _settings = settings;

    /// <summary>Applies edited settings; the next decision uses them.</summary>
    public void Update(AudioSwitchSettings settings) => _settings = settings;

    public DeviceProfile? ProfileFor(AudioEndpoint output) =>
        _settings.Profiles.FirstOrDefault(p => output.Name.Contains(p.Match, StringComparison.OrdinalIgnoreCase));

    public string DisplayName(AudioEndpoint output) => ProfileFor(output)?.Name ?? output.DeviceName;

    /// <summary>Key for "recently used": stable across the Arctis getting a new endpoint id ("8- Arctis…").</summary>
    public string RecentKey(AudioEndpoint output) => ProfileFor(output)?.Name ?? output.DeviceName;

    public (AudioEndpoint? Mic, string Reason) PickMic(AudioEndpoint output, AvailabilitySnapshot snapshot)
    {
        var profile = ProfileFor(output);
        var preference = profile is { MicPreference.Count: > 0 } ? profile.MicPreference : _settings.DefaultMicPreference;
        if (preference.Count == 0) return (null, $"no mic preference for {DisplayName(output)}, mic unchanged");
        var list = $"[{string.Join(", ", preference)}]";
        foreach (var fragment in preference)
            if (snapshot.Mics.FirstOrDefault(m => m.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)) is { } mic)
                return (mic, $"first available of {list} for {DisplayName(output)}");
        return (null, $"none of {list} available, mic unchanged");
    }

    /// <summary>Everything to <paramref name="output"/>: Windows defaults (per profile), all Sonar outputs of
    /// <paramref name="mode"/> (the four channels in classic mode, the personal mix in stream mode), and the mic its
    /// profile prefers.</summary>
    public SwitchPlan PlanSwitchTo(AudioEndpoint output, AvailabilitySnapshot snapshot, string reason, SonarMode mode = SonarMode.Classic)
    {
        var (mic, micReason) = PickMic(output, snapshot);
        var (windows, windowsReason) = PickWindowsDefaults(output, snapshot);
        return new SwitchPlan(output, windows, SonarChannels.OutputsFor(mode), mic, $"{reason}; mic: {micReason}; Windows: {windowsReason}");
    }

    /// <summary>Everything to <paramref name="output"/> for the way audio is routed now: like the overload above with
    /// Sonar, or in Windows-only mode just the Windows defaults (never a Sonar virtual device) and the profile's mic as
    /// the Windows recording default.</summary>
    public SwitchPlan PlanSwitchTo(AudioEndpoint output, AvailabilitySnapshot snapshot, string reason, CurrentRouting current)
    {
        if (!current.WindowsOnly) return PlanSwitchTo(output, snapshot, reason, current.Mode);
        var (mic, micReason) = PickMic(output, snapshot);
        var (windows, windowsReason) = PickWindowsDefaults(output, snapshot, windowsOnly: true);
        return new SwitchPlan(output, windows, [], mic, $"{reason}; mic: {micReason}; Windows: {windowsReason}") { WindowsOnly = true };
    }

    /// <summary>The profile's WindowsDefault/WindowsCommunicationsDefault endpoints (e.g. Sonar - Gaming / Sonar - Chat
    /// for the SteelSeries headset), else the device itself. In <paramref name="windowsOnly"/> mode Sonar's virtual
    /// devices are skipped: without Sonar running, audio sent there goes nowhere.</summary>
    public (WindowsDefaults Defaults, string Reason) PickWindowsDefaults(AudioEndpoint output, AvailabilitySnapshot snapshot, bool windowsOnly = false)
    {
        var profile = ProfileFor(output);
        var notes = new List<string>();

        AudioEndpoint Resolve(string? fragment, AudioEndpoint fallback, string role)
        {
            if (fragment is null) return fallback;
            if (snapshot.FindActive(fragment, EndpointFlow.Render) is { } found)
            {
                if (!windowsOnly || !IsSonarVirtual(found)) return found;
                notes.Add($"'{fragment}' is a Sonar device and Sonar isn't running, using {fallback.Name} for {role}");
                return fallback;
            }
            notes.Add($"'{fragment}' not found for {role}, using {fallback.Name}");
            return fallback;
        }

        var playback = Resolve(profile?.WindowsDefault, output, "default");
        var communications = Resolve(profile?.WindowsCommunicationsDefault, playback, "communications");
        var defaults = WindowsDefaults.For(playback, communications, snapshot.Active);
        var reason = profile?.WindowsDefault is null ? "the device itself" : $"{DisplayName(output)} profile";
        return (defaults, notes.Count == 0 ? reason : $"{reason} ({string.Join("; ", notes)})");
    }

    /// <summary>Best usable output: most recently used first, then profile order, then name.</summary>
    public (AudioEndpoint? Output, string Reason) PickFallbackOutput(AvailabilitySnapshot snapshot, IReadOnlyList<string> recent)
    {
        var ranked = snapshot.Outputs
            .Select(o => (Output: o, Recent: IndexOrMax(recent, RecentKey(o)), Profile: ProfileIndex(o)))
            .OrderBy(x => x.Recent).ThenBy(x => x.Profile).ThenBy(x => x.Output.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ranked.Count == 0) return (null, "no usable output");

        var best = ranked[0];
        var why = best.Recent != int.MaxValue ? "most recently used"
            : best.Profile != int.MaxValue ? "first configured device"
            : ranked.Count > 1 ? "first by name"
            : "only usable output";
        return (best.Output, why);
    }

    static readonly string SonarVirtualDeviceName = new DeviceFilterOptions().SonarVirtualDeviceName;

    static bool IsSonarVirtual(AudioEndpoint endpoint) =>
        endpoint.DeviceName.Equals(SonarVirtualDeviceName, StringComparison.OrdinalIgnoreCase);

    int ProfileIndex(AudioEndpoint output) => ProfileFor(output) is { } p ? IndexOrMax(_settings.Profiles, p) : int.MaxValue;

    static int IndexOrMax<T>(IReadOnlyList<T> list, T item)
    {
        for (var i = 0; i < list.Count; i++)
            if (EqualityComparer<T>.Default.Equals(list[i], item)) return i;
        return int.MaxValue;
    }
}
