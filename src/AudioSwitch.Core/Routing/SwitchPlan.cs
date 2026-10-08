using AudioSwitch.Core.Audio;
using AudioSwitch.Core.Settings;
using AudioSwitch.Core.Sonar;

namespace AudioSwitch.Core.Routing;

/// <summary>Windows default playback devices to set: <see cref="Default"/> for the default + multimedia roles,
/// <see cref="Communications"/> for calls, e.g. Sonar's virtual Gaming and Chat devices for a SteelSeries headset.</summary>
public sealed record WindowsDefaults(AudioEndpoint Default, AudioEndpoint Communications)
{
    /// <summary>Names for display; <see cref="For"/> shortens them. Not part of equality.</summary>
    public string DefaultName { get; init; } = Default.Name;

    /// <inheritdoc cref="DefaultName"/>
    public string CommunicationsName { get; init; } = Communications.Name;

    /// <summary>Defaults with short display names (see <see cref="ShortName"/>) among the <paramref name="active"/> endpoints.</summary>
    public static WindowsDefaults For(AudioEndpoint playback, AudioEndpoint communications, IEnumerable<AudioEndpoint> active)
    {
        var list = active.ToList();
        return new(playback, communications) { DefaultName = ShortName(playback, list), CommunicationsName = ShortName(communications, list) };
    }

    /// <summary>The endpoint's label without the " (device name)" suffix when no other active endpoint shares it
    /// ("SteelSeries Sonar - Gaming"); otherwise the full name, since many endpoints are just called "Headphones"
    /// or "Speakers".</summary>
    public static string ShortName(AudioEndpoint e, IEnumerable<AudioEndpoint> active)
    {
        var label = FragmentSuggestions.EndpointLabel(e);
        return active.Any(o => o.Id != e.Id && o.Flow == e.Flow && FragmentSuggestions.EndpointLabel(o).Equals(label, StringComparison.OrdinalIgnoreCase))
            ? e.Name
            : label;
    }

    public bool Equals(WindowsDefaults? other) => other is not null && Default == other.Default && Communications == other.Communications;

    public override int GetHashCode() => HashCode.Combine(Default, Communications);

    public override string ToString() =>
        Default.Id == Communications.Id ? DefaultName : $"{DefaultName}, calls: {CommunicationsName}";
}

/// <summary>A concrete change to make. <see cref="Output"/> null = mic-only change; <see cref="WindowsDefaults"/>
/// null = leave Windows' defaults alone. <see cref="Channels"/> are the Sonar output channels of the mode the plan was
/// made in; the executor translates them if Sonar switched modes since (<see cref="SonarChannels.ForMode"/>).</summary>
public sealed record SwitchPlan(
    AudioEndpoint? Output,
    WindowsDefaults? WindowsDefaults,
    IReadOnlyList<SonarChannel> Channels,
    AudioEndpoint? Mic,
    string Reason)
{
    /// <summary>Made in Windows-only mode: no Sonar channels, and <see cref="Mic"/> becomes the Windows default
    /// recording device instead of Sonar's mic.</summary>
    public bool WindowsOnly { get; init; }

    public override string ToString()
    {
        var parts = new List<string>();
        if (WindowsDefaults is not null) parts.Add($"Windows default → {WindowsDefaults}");
        if (Output is not null && Channels.Count > 0) parts.Add($"Sonar {string.Join("+", Channels)} → {Output.Name}");
        if (Mic is not null) parts.Add($"{(WindowsOnly ? "Windows recording default" : "mic")} → {Mic.Name}");
        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }
}

/// <summary>Where audio goes right now, in Sonar's current <see cref="Mode"/>: <see cref="Sonar"/> holds that mode's
/// redirections (Game..Aux + Mic in classic mode, Monitoring + Mic in stream mode). Sonar clears a redirection's device
/// id ("") when its device disappears from Windows (e.g. a Bluetooth device turning off). <see cref="SonarKnown"/> is
/// false when Sonar couldn't be read. In Windows-only mode (<see cref="FromWindows"/>) the Windows defaults are the
/// routing.</summary>
public sealed record CurrentRouting(
    IReadOnlyDictionary<SonarChannel, string> Sonar, string? WindowsDefaultOutput, bool SonarKnown = true, SonarMode Mode = SonarMode.Classic)
{
    public static readonly CurrentRouting Unknown = new(new Dictionary<SonarChannel, string>(), null, SonarKnown: false);

    /// <summary>No Sonar: the output is the Windows default playback device, the mic the default recording device.</summary>
    public bool WindowsOnly { get; init; }

    /// <summary>Windows-only mode: the default recording device.</summary>
    public string? WindowsDefaultMic { get; init; }

    /// <summary>Windows-only mode: the defaults before the change being handled. Windows moves a default elsewhere by
    /// itself when its device goes away, so only these tell whether the device that left was in use.</summary>
    public string? PreviousWindowsOutput { get; init; }

    /// <inheritdoc cref="PreviousWindowsOutput"/>
    public string? PreviousWindowsMic { get; init; }

    /// <summary>Windows-only routing; <paramref name="before"/> is the snapshot from before the change, if any.</summary>
    public static CurrentRouting FromWindows(string? output, string? mic, Watching.AvailabilitySnapshot? before = null) =>
        Unknown with
        {
            WindowsDefaultOutput = output,
            WindowsOnly = true,
            WindowsDefaultMic = mic,
            PreviousWindowsOutput = before?.DefaultOutputId,
            PreviousWindowsMic = before?.DefaultMicId,
        };

    /// <summary>Whether the engine may act: Sonar's routing was read, or there is no Sonar to read.</summary>
    public bool Known => SonarKnown || WindowsOnly;

    /// <summary>The channels that decide what the user hears in <see cref="Mode"/>; none in Windows-only mode.</summary>
    public IReadOnlyList<SonarChannel> OutputChannels => WindowsOnly ? [] : SonarChannels.OutputsFor(Mode);

    /// <summary>The device the user hears: through Sonar, Game in classic mode or the personal mix in stream mode;
    /// in Windows-only mode the Windows default.</summary>
    public string? Output => WindowsOnly ? WindowsDefaultOutput : Sonar.GetValueOrDefault(OutputChannels[0]);

    public string? Mic => WindowsOnly ? WindowsDefaultMic : Sonar.GetValueOrDefault(SonarChannel.Mic);

    /// <summary>Output channels whose device is cleared or not usable; empty when Sonar's routing is unknown.</summary>
    public IReadOnlyList<SonarChannel> BrokenOutputChannels(Watching.AvailabilitySnapshot snapshot) =>
        SonarKnown
            ? OutputChannels.Where(c => Sonar.GetValueOrDefault(c) is not { Length: > 0 } id || !snapshot.Outputs.Any(o => o.Id == id)).ToList()
            : [];

    /// <summary>The output is cleared or not usable: a broken Sonar channel, or in Windows-only mode no default at all
    /// although usable outputs exist. A Windows default on an ignored device is the user's choice and left alone
    /// (Windows always keeps some default, and many people play through an "(HDMI)" TV on purpose).</summary>
    public bool OutputBroken(Watching.AvailabilitySnapshot snapshot) =>
        WindowsOnly ? WindowsDefaultOutput is null && snapshot.Outputs.Count > 0 : BrokenOutputChannels(snapshot).Count > 0;

    /// <summary>The mic is cleared or not usable (false when Sonar's routing is unknown). Windows-only: no recording
    /// default although usable mics exist.</summary>
    public bool MicBroken(Watching.AvailabilitySnapshot snapshot) =>
        WindowsOnly ? WindowsDefaultMic is null && snapshot.Mics.Count > 0
            : SonarKnown && (Mic is not { Length: > 0 } id || !snapshot.IsUsable(id));

    public bool UsesOutput(string endpointId) =>
        WindowsDefaultOutput == endpointId || OutputChannels.Any(c => Sonar.GetValueOrDefault(c) == endpointId)
        || (WindowsOnly && PreviousWindowsOutput == endpointId);

    public bool UsesMic(string endpointId) => Mic == endpointId || (WindowsOnly && PreviousWindowsMic == endpointId);

    /// <summary>True when applying <paramref name="plan"/> would change nothing.</summary>
    public bool AlreadyMatches(SwitchPlan plan) =>
        (plan.WindowsDefaults is null || plan.WindowsDefaults.Default.Id == WindowsDefaultOutput)
        && (plan.Output is null || SonarChannels.ForMode(plan.Channels, Mode).All(c => Sonar.GetValueOrDefault(c) == plan.Output.Id))
        && (plan.Mic is null || plan.Mic.Id == Mic);
}

public abstract record RoutingDecision(string Reason);

/// <summary>Ask the user whether to apply <see cref="Plan"/>.</summary>
public sealed record PromptDecision(AudioEndpoint Device, string DisplayName, SwitchPlan Plan, bool DisconnectWhenDeclined, string Reason)
    : RoutingDecision(Reason);

/// <summary>Apply <see cref="Plan"/> without asking (fallback after a device went away). <see cref="Cause"/> is the
/// short "why" for the notification, e.g. "SteelSeries Arctis headset powered off".</summary>
public sealed record AutoSwitchDecision(SwitchPlan Plan, string Cause, string Reason) : RoutingDecision(Reason);

/// <summary>Disconnect a Bluetooth device from this PC (it stays paired).</summary>
public sealed record DisconnectDecision(AudioEndpoint Device, string DisplayName, string Reason) : RoutingDecision(Reason);

public sealed record NoActionDecision(string Reason) : RoutingDecision(Reason);
