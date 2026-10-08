using AudioSwitch.Core.Audio;

namespace AudioSwitch.Core.Watching;

public sealed record ExcludedEndpoint(AudioEndpoint Endpoint, string Reason, bool IsHeadsetOff);

public enum DeviceChangeKind { Arrived, Departed }

public sealed record DeviceChange(DeviceChangeKind Kind, AudioEndpoint Endpoint, string Reason)
{
    public override string ToString() => $"{Endpoint.Name} {(Kind == DeviceChangeKind.Arrived ? "available" : "gone")} ({Reason})";
}

/// <summary>
/// The outputs and mics that can be used right now. An endpoint is usable when Windows reports it active, the
/// <see cref="DeviceFilter"/> allows it and, for SteelSeries wireless devices, the headset is powered on: the base
/// station's USB endpoint stays active when the headset is off, so only Sonar can tell.
/// </summary>
public sealed record AvailabilitySnapshot(
    IReadOnlyList<AudioEndpoint> Outputs,
    IReadOnlyList<AudioEndpoint> Mics,
    IReadOnlyList<ExcludedEndpoint> Excluded)
{
    public static readonly AvailabilitySnapshot Empty = new([], [], []);

    /// <summary>The Windows default playback endpoint (console role) when the snapshot was taken. Windows moves its
    /// defaults by itself when the default device goes away, so in Windows-only mode "was it in use?" is answered by
    /// the snapshot from before the change.</summary>
    public string? DefaultOutputId { get; init; }

    /// <summary>The Windows default recording endpoint (console role) when the snapshot was taken.</summary>
    public string? DefaultMicId { get; init; }

    /// <param name="wirelessStates">Endpoint id → headset connected, from Sonar; null when unknown (Sonar not
    /// reachable), in which case SteelSeries endpoints are treated as usable.</param>
    public static AvailabilitySnapshot Build(
        IEnumerable<AudioEndpoint> activeEndpoints, IReadOnlyDictionary<string, bool>? wirelessStates, DeviceFilter filter)
    {
        var outputs = new List<AudioEndpoint>();
        var mics = new List<AudioEndpoint>();
        var excluded = new List<ExcludedEndpoint>();
        foreach (var endpoint in activeEndpoints.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (filter.GetExclusionReason(endpoint) is { } reason)
                excluded.Add(new ExcludedEndpoint(endpoint, reason, IsHeadsetOff: false));
            else if (wirelessStates is not null && wirelessStates.TryGetValue(endpoint.Id, out var headsetOn) && !headsetOn)
                excluded.Add(new ExcludedEndpoint(endpoint, "SteelSeries wireless headset is off", IsHeadsetOff: true));
            else
                (endpoint.Flow == EndpointFlow.Render ? outputs : mics).Add(endpoint);
        }
        return new AvailabilitySnapshot(outputs, mics, excluded);
    }

    public IEnumerable<AudioEndpoint> Usable => Outputs.Concat(Mics);

    public bool IsUsable(string endpointId) => Usable.Any(e => e.Id == endpointId);

    /// <summary>Every active endpoint, usable or not (e.g. Sonar's virtual devices).</summary>
    public IEnumerable<AudioEndpoint> Active => Usable.Concat(Excluded.Select(x => x.Endpoint));

    /// <summary>Any active endpoint, usable or not (e.g. Sonar's virtual devices), whose name contains the fragment.</summary>
    public AudioEndpoint? FindActive(string nameFragment, EndpointFlow flow) =>
        Active.FirstOrDefault(e => e.Flow == flow && e.Name.Contains(nameFragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>What became usable or unusable compared to <paramref name="previous"/>, with the reason.</summary>
    public IReadOnlyList<DeviceChange> DiffFrom(AvailabilitySnapshot previous)
    {
        var before = previous.Usable.ToDictionary(e => e.Id);
        var after = Usable.ToDictionary(e => e.Id);
        var changes = new List<DeviceChange>();

        foreach (var (id, endpoint) in after)
        {
            if (before.ContainsKey(id)) continue;
            var wasHeadsetOff = previous.Excluded.Any(x => x.Endpoint.Id == id && x.IsHeadsetOff);
            changes.Add(new DeviceChange(DeviceChangeKind.Arrived, endpoint, wasHeadsetOff ? "headset powered on" : "connected"));
        }
        foreach (var (id, endpoint) in before)
        {
            if (after.ContainsKey(id)) continue;
            var reason = Excluded.FirstOrDefault(x => x.Endpoint.Id == id) is { } nowExcluded
                ? nowExcluded.IsHeadsetOff ? "headset powered off" : nowExcluded.Reason
                : "disconnected";
            changes.Add(new DeviceChange(DeviceChangeKind.Departed, endpoint, reason));
        }
        return changes;
    }
}
