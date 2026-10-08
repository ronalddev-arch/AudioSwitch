using AudioSwitch.Core.Audio;

namespace AudioSwitch.Core.Settings;

/// <summary>
/// Name fragments the settings window offers for the active endpoints. Settings match endpoints by a fragment of their
/// name, so the suggestions avoid the parts that change: the "8- " instance prefix of a re-enumerated USB device.
/// </summary>
public static class FragmentSuggestions
{
    /// <summary>For a profile's Match, mic preferences and exclusions: the device names, e.g. "Arctis Nova Pro Wireless".</summary>
    public static IReadOnlyList<string> DeviceNames(IEnumerable<AudioEndpoint> endpoints, EndpointFlow flow) =>
        endpoints.Where(e => e.Flow == flow).Select(e => e.DisplayDeviceName).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// For a profile's Windows defaults, which may be virtual endpoints of one device: the endpoint's own label
    /// ("SteelSeries Sonar - Gaming") when no other endpoint shares it, otherwise the device name (many endpoints
    /// are just called "Speakers" or "Headphones").
    /// </summary>
    public static IReadOnlyList<string> EndpointLabels(IEnumerable<AudioEndpoint> endpoints, EndpointFlow flow)
    {
        var list = endpoints.Where(e => e.Flow == flow).ToList();
        var labelCounts = list.GroupBy(EndpointLabel, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        return list.Select(e => labelCounts[EndpointLabel(e)] == 1 ? EndpointLabel(e) : e.DisplayDeviceName)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The endpoint's own label, without the device name Windows appends:
    /// "SteelSeries Sonar - Gaming (SteelSeries Sonar Virtual Audio Device)" → "SteelSeries Sonar - Gaming".</summary>
    public static string EndpointLabel(AudioEndpoint e) =>
        e.Name.EndsWith($" ({e.DeviceName})", StringComparison.OrdinalIgnoreCase) ? e.Name[..^(e.DeviceName.Length + 3)] : e.Name;
}
