using AudioSwitch.Core.Audio;
using AudioSwitch.Core.Routing;
using AudioSwitch.Core.Watching;

namespace AudioSwitch.Core.Settings;

/// <summary>
/// What the settings window's device picker shows and the profile it creates for a picked output. Only facts about
/// the device itself go in, plus Sonar's Gaming/Chat as Windows defaults when the user chose that
/// (<see cref="AudioSwitchSettings.NewDevicesUseSonarDefaults"/>); other per-brand choices (disconnecting a shared
/// headset) stay with the user.
/// </summary>
public static class ProfileSuggestions
{
    // Never user-edited (settings only change the name fragments), so the defaults are the real values.
    static readonly DeviceFilterOptions Filter = new();

    /// <summary>The container Windows gives every device built into the PC (onboard sound, internal mic), so it does not
    /// identify one physical device.</summary>
    static readonly Guid LocalMachineContainer = new("00000000-0000-0000-ffff-ffffffffffff");

    /// <summary>The outputs a profile can be made for: everything but Sonar's virtual devices (routing sources) and
    /// Bluetooth Hands-Free endpoints (never used), and nameless endpoints, which no profile could ever match. Sorted by
    /// device name.</summary>
    public static IReadOnlyList<AudioEndpoint> PickableOutputs(IEnumerable<AudioEndpoint> activeEndpoints) =>
        activeEndpoints.Where(e => e.Flow == EndpointFlow.Render && IsRealDevice(e) && !string.IsNullOrWhiteSpace(e.Name))
            .OrderBy(e => e.DisplayDeviceName, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>A new profile for <paramref name="output"/>: named and matched by <see cref="FriendlyName"/>, with the
    /// device's own microphone if it has one, otherwise the default microphone order. Windows defaults: Sonar's Gaming
    /// and Chat devices with <paramref name="sonarDefaults"/> (see <see cref="UseSonarDefaults"/>), else the device
    /// itself. Without Sonar running, routing skips Sonar's devices and uses the device itself anyway.</summary>
    public static DeviceProfile FromEndpoint(AudioEndpoint output, IEnumerable<AudioEndpoint> activeEndpoints, bool sonarDefaults = false)
    {
        var endpoints = activeEndpoints.ToList();
        var name = FriendlyName(output, endpoints);
        var mic = OwnMicrophone(output, endpoints);
        return new DeviceProfile
        {
            Name = name,
            Match = output.Name.Contains(name, StringComparison.OrdinalIgnoreCase) ? name : output.Name.Trim(),
            MicPreference = mic is null ? [] : [mic.DisplayDeviceName],
            WindowsDefault = sonarDefaults ? DeviceFilterOptions.SonarGamingEndpoint : null,
            WindowsCommunicationsDefault = sonarDefaults ? DeviceFilterOptions.SonarChatEndpoint : null,
            DisconnectWhenDeclined = false,
        };
    }

    /// <summary>Whether Sonar is, or may be, what apps play through: found, or GG installed but Sonar not answering yet
    /// (<see cref="SwitchingMode.Undetermined"/>). Not when it is known to be absent, or nothing was checked yet (null).</summary>
    public static bool SonarMayBeInUse(SwitchingMode? mode) => mode is SwitchingMode.Sonar or SwitchingMode.Undetermined;

    /// <summary>Whether a new profile gets Sonar's Gaming/Chat as Windows defaults: the user wants that
    /// (<see cref="AudioSwitchSettings.NewDevicesUseSonarDefaults"/>) and <see cref="SonarMayBeInUse"/>.</summary>
    public static bool UseSonarDefaults(bool newDevicesUseSonarDefaults, SwitchingMode? mode) =>
        newDevicesUseSonarDefaults && SonarMayBeInUse(mode);

    /// <summary>
    /// What identifies <paramref name="output"/> among the active outputs: its device name without the USB "8- " prefix,
    /// unless other outputs share that device name (one GPU audio device serving several monitors, all "NVIDIA High
    /// Definition Audio"); then its own endpoint label ("32G2WG8") if no other output has that label, else the device name.
    /// Some virtual devices (Windows Sandbox's "Remote Audio") report no device name at all; then the endpoint's own name.
    /// </summary>
    public static string FriendlyName(AudioEndpoint output, IEnumerable<AudioEndpoint> activeEndpoints)
    {
        if (string.IsNullOrWhiteSpace(output.DisplayDeviceName)) return output.Name.Trim();
        var others = activeEndpoints.Where(e => e.Flow == EndpointFlow.Render && e.Id != output.Id).ToList();
        if (!others.Any(e => e.DisplayDeviceName.Equals(output.DisplayDeviceName, StringComparison.OrdinalIgnoreCase)))
            return output.DisplayDeviceName;
        var label = FragmentSuggestions.EndpointLabel(output);
        return others.Any(e => FragmentSuggestions.EndpointLabel(e).Equals(label, StringComparison.OrdinalIgnoreCase))
            ? output.DisplayDeviceName
            : label;
    }

    /// <summary>A capture endpoint of the same physical device: the same container id when both have one (and it is not
    /// the PC's own container), otherwise the same device name. Hands-Free mics don't count.</summary>
    public static AudioEndpoint? OwnMicrophone(AudioEndpoint output, IEnumerable<AudioEndpoint> activeEndpoints) =>
        activeEndpoints.FirstOrDefault(e => e.Flow == EndpointFlow.Capture && IsRealDevice(e) && SamePhysicalDevice(output, e));

    /// <summary>The first profile that would claim <paramref name="output"/> (same rule as routing; blank matches,
    /// which a profile still being edited can have, claim nothing).</summary>
    public static DeviceProfile? MatchingProfile(AudioEndpoint output, IEnumerable<DeviceProfile> profiles) =>
        profiles.FirstOrDefault(p => p.Match.Trim().Length > 0 && output.Name.Contains(p.Match.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>How the output is connected, for display: like <see cref="ConnectionType(string)"/>, but monitor and TV
    /// audio (<see cref="MonitorAudio"/>) is told apart from the PC's built-in sound, which share the HDAUDIO bus.</summary>
    public static string ConnectionType(AudioEndpoint output) =>
        MonitorAudio.IsMonitorAudio(output) ? "Monitor/TV"
        : output.Bus.Equals("HDAUDIO", StringComparison.OrdinalIgnoreCase) ? "Built-in"
        : ConnectionType(output.Bus);

    /// <summary>How the device is connected, from its PnP bus, for display.</summary>
    public static string ConnectionType(string bus) => bus.ToUpperInvariant() switch
    {
        "BTHENUM" => "Bluetooth",
        "USB" => "USB",
        "HDAUDIO" => "Built-in/HDMI",
        _ => bus,
    };

    static bool IsRealDevice(AudioEndpoint e) =>
        !e.DeviceName.Equals(Filter.SonarVirtualDeviceName, StringComparison.OrdinalIgnoreCase)
        && !Filter.ExcludedBuses.Contains(e.Bus, StringComparer.OrdinalIgnoreCase);

    static bool SamePhysicalDevice(AudioEndpoint a, AudioEndpoint b) =>
        a.ContainerId is { } ca && b.ContainerId is { } cb && ca != LocalMachineContainer && cb != LocalMachineContainer
            ? ca == cb
            : a.DisplayDeviceName.Equals(b.DisplayDeviceName, StringComparison.OrdinalIgnoreCase);
}
