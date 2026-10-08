using AudioSwitch.Core.Audio;

namespace AudioSwitch.Core.Settings;

/// <summary>One connected output as the first-run setup lists it.</summary>
/// <param name="Name">What the profile would be called (<see cref="ProfileSuggestions.FriendlyName"/>).</param>
/// <param name="Connection">"Bluetooth", "USB", "Built-in", "Monitor/TV" or "Other" (no bus names), for display.</param>
/// <param name="Ticked">Whether the setup suggests a profile for it.</param>
public sealed record FirstRunDevice(AudioEndpoint Output, string Name, string Connection, bool Ticked);

/// <summary>What the user chose in the first-run setup. <paramref name="Devices"/> are the ticked outputs, in the
/// fallback order the user arranged.</summary>
/// <param name="KeepWindowsOnSonar">The "Keep sound going through SteelSeries Sonar" answer, saved as
/// <see cref="AudioSwitchSettings.NewDevicesUseSonarDefaults"/>.</param>
/// <param name="SonarFound">Whether Sonar was found or GG was starting (<see cref="ProfileSuggestions.SonarMayBeInUse"/>),
/// so that the question was shown: only then does the answer apply to the profiles made now.</param>
public sealed record FirstRunChoices(IReadOnlyList<AudioEndpoint> Devices, bool UseMonitorSpeakers, bool CheckForUpdates,
    bool KeepWindowsOnSonar, bool SonarFound);

/// <summary>
/// The first-run setup's suggestions and the settings it produces. It only fills in settings; the device itself, the
/// rules and the routing are the same as for settings made in the settings window.
/// </summary>
public static class FirstRunSetup
{
    /// <summary>The connected outputs that play through a monitor or TV, for the "Use monitor/TV speakers?" question.</summary>
    public static IReadOnlyList<AudioEndpoint> Monitors(IEnumerable<AudioEndpoint> activeEndpoints) =>
        ProfileSuggestions.PickableOutputs(activeEndpoints).Where(MonitorAudio.IsMonitorAudio).ToList();

    /// <summary>
    /// The outputs to list, in the suggested fallback order: Bluetooth and USB devices (headsets, speakers), then the
    /// PC's built-in sound, then monitors and TVs, then anything else (usually virtual devices of other software), each
    /// group by name. Physical devices are ticked; monitors only with <paramref name="useMonitorSpeakers"/>, and without
    /// it they are left out altogether (they will be ignored).
    /// </summary>
    public static IReadOnlyList<FirstRunDevice> Devices(IReadOnlyList<AudioEndpoint> activeEndpoints, bool useMonitorSpeakers) =>
        ProfileSuggestions.PickableOutputs(activeEndpoints)
            .Select(e => (Output: e, Group: Group(e)))
            .Where(x => useMonitorSpeakers || x.Group != DeviceGroup.Monitor)
            .OrderBy(x => x.Group) // stable: keeps PickableOutputs' name order within a group
            .Select(x => new FirstRunDevice(x.Output, ProfileSuggestions.FriendlyName(x.Output, activeEndpoints),
                x.Group == DeviceGroup.Other ? "Other" : ProfileSuggestions.ConnectionType(x.Output), x.Group != DeviceGroup.Other))
            .ToList();

    /// <summary>
    /// The settings to save: <paramref name="defaults"/> with a profile per chosen device (filled in like Settings →
    /// Devices → Add; a device an earlier profile already matches is skipped), the monitor answer applied to the ignored
    /// devices, and the update-check and Sonar choices. The profiles get Sonar's Gaming/Chat as Windows defaults when
    /// Sonar was found and the user kept sound going through it.
    /// <list type="bullet">
    /// <item>Monitor speakers not used: each connected monitor's <see cref="MonitorAudio.ExclusionFragment"/> is added.</item>
    /// <item>Used: every ignored fragment that hides a connected monitor, and <see cref="MonitorAudio.GenericHdmiFragment"/>, is removed.</item>
    /// </list>
    /// </summary>
    public static AudioSwitchSettings Build(AudioSwitchSettings defaults, FirstRunChoices choices, IReadOnlyList<AudioEndpoint> activeEndpoints)
    {
        var monitors = Monitors(activeEndpoints);
        var excluded = choices.UseMonitorSpeakers
            ? defaults.ExcludedNameFragments.Where(f =>
                !f.Equals(MonitorAudio.GenericHdmiFragment, StringComparison.OrdinalIgnoreCase)
                && !monitors.Any(m => m.Name.Contains(f, StringComparison.OrdinalIgnoreCase))).ToList()
            : [.. defaults.ExcludedNameFragments, .. monitors.Select(MonitorAudio.ExclusionFragment)];

        var sonarDefaults = choices.KeepWindowsOnSonar && choices.SonarFound;
        var profiles = new List<DeviceProfile>();
        foreach (var output in choices.Devices)
        {
            if (excluded.Any(f => output.Name.Contains(f, StringComparison.OrdinalIgnoreCase))) continue; // would never be used
            if (ProfileSuggestions.MatchingProfile(output, profiles) is not null) continue; // e.g. two identical USB devices
            var profile = ProfileSuggestions.FromEndpoint(output, activeEndpoints, sonarDefaults);
            if (profile.Name.Trim().Length == 0 || profile.Match.Trim().Length == 0) continue; // nameless: could never match
            if (profiles.Any(p => p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase))) continue;
            profiles.Add(profile);
        }

        return (defaults with
        {
            Profiles = profiles,
            ExcludedNameFragments = excluded,
            CheckForUpdatesAutomatically = choices.CheckForUpdates,
            NewDevicesUseSonarDefaults = choices.KeepWindowsOnSonar,
        }).Normalize();
    }

    enum DeviceGroup { External, BuiltIn, Monitor, Other }

    static DeviceGroup Group(AudioEndpoint output) =>
        MonitorAudio.IsMonitorAudio(output) ? DeviceGroup.Monitor
        : output.Bus.ToUpperInvariant() switch
        {
            "BTHENUM" or "USB" => DeviceGroup.External,
            "HDAUDIO" => DeviceGroup.BuiltIn,
            _ => DeviceGroup.Other,
        };
}
