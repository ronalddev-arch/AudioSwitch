using AudioSwitch.Core.Audio;

namespace AudioSwitch.Core.Watching;

public sealed record DeviceFilterOptions
{
    /// <summary>Case-insensitive fragments of the endpoint name that are never offered or used.</summary>
    /// <remarks>These are the defaults for a settings.json written without the first-run setup (it was closed without
    /// finishing). The setup asks "Use monitor/TV speakers?": No adds the connected monitors' own fragments, Yes drops
    /// <see cref="MonitorAudio.GenericHdmiFragment"/> (<see cref="Settings.FirstRunSetup"/>).</remarks>
    public IReadOnlyList<string> ExcludedNameFragments { get; init; } =
    [
        MonitorAudio.GenericHdmiFragment, // Windows' generic HDMI audio (monitors and TVs; often without speakers)
        "Hands-Free",                     // Bluetooth HFP: mono, low quality
    ];

    /// <summary>PnP buses that are never used. BTHHFENUM = Bluetooth hands-free profile.</summary>
    public IReadOnlyList<string> ExcludedBuses { get; init; } = ["BTHHFENUM"];

    /// <summary>Sonar's own virtual endpoints are routing sources, never targets.</summary>
    public string SonarVirtualDeviceName { get; init; } = "SteelSeries Sonar Virtual Audio Device";

    /// <summary>Sonar's virtual Gaming playback endpoint (of <see cref="SonarVirtualDeviceName"/>), as a name fragment.
    /// With Sonar in use, apps should play here (or to <see cref="SonarChatEndpoint"/>) so that Sonar's EQ and mixer
    /// apply; Sonar then routes its channels to the physical device.</summary>
    public const string SonarGamingEndpoint = "SteelSeries Sonar - Gaming";

    /// <summary>Sonar's virtual Chat playback endpoint, as a name fragment: the Windows communications default (calls)
    /// when Sonar is in use.</summary>
    public const string SonarChatEndpoint = "SteelSeries Sonar - Chat";
}

public sealed class DeviceFilter(DeviceFilterOptions options)
{
    volatile DeviceFilterOptions _options = options;

    /// <summary>Applies edited options; see <see cref="AudioWatcher.RefreshAsync"/> to re-evaluate devices with them.</summary>
    public void Update(DeviceFilterOptions options) => _options = options;

    /// <summary>Returns null when the endpoint may be used, otherwise a human-readable reason for the log.</summary>
    public string? GetExclusionReason(AudioEndpoint endpoint)
    {
        var options = _options;
        if (endpoint.DeviceName.Equals(options.SonarVirtualDeviceName, StringComparison.OrdinalIgnoreCase))
            return "Sonar virtual device";
        if (options.ExcludedBuses.FirstOrDefault(b => b.Equals(endpoint.Bus, StringComparison.OrdinalIgnoreCase)) is { } bus)
            return $"excluded bus {bus}";
        if (options.ExcludedNameFragments.FirstOrDefault(f => endpoint.Name.Contains(f, StringComparison.OrdinalIgnoreCase)) is { } fragment)
            return $"name matches excluded '{fragment}'";
        return null;
    }
}
