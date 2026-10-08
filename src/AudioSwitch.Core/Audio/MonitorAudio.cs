using System.Text.RegularExpressions;

namespace AudioSwitch.Core.Audio;

/// <summary>
/// Recognises outputs that play through a monitor or TV (HDMI/DisplayPort audio from the graphics card). Many monitors
/// have no speakers, so the first-run setup asks whether to use them. Conservative: the bus alone says nothing (built-in
/// speakers are HDAUDIO too, and so is the generic "High Definition Audio Device" driver for onboard sound), so only the
/// graphics drivers' display-audio device names and an explicit HDMI/DisplayPort in the name count.
/// </summary>
public static partial class MonitorAudio
{
    /// <summary>The name fragment Windows' generic HD Audio driver uses for HDMI ("Digital Audio (HDMI)"); ignored by
    /// default until the user says otherwise.</summary>
    public const string GenericHdmiFragment = "(HDMI)";

    /// <summary>Display-audio device names of the graphics drivers. Every monitor on that graphics card is an endpoint
    /// of this device, labelled with the monitor's name ("32G2WG8 (NVIDIA High Definition Audio)") or a generic one
    /// ("NVIDIA Output").</summary>
    static readonly string[] DisplayAudioDevices =
    [
        "NVIDIA High Definition Audio",
        "AMD High Definition Audio Device",
        "ATI HDMI Audio",
        "Intel(R) Display Audio",
        "Intel® Display Audio",
    ];

    [GeneratedRegex(@"\bHDMI\b|\bDisplayPort\b", RegexOptions.IgnoreCase)]
    private static partial Regex MonitorConnection();

    /// <summary>True for an output that plays through a monitor or TV. Microphones never count (an HDMI capture card is
    /// not a speaker).</summary>
    public static bool IsMonitorAudio(AudioEndpoint endpoint) =>
        endpoint.Flow == EndpointFlow.Render && (DisplayAudioDevice(endpoint) is not null || MonitorConnection().IsMatch(endpoint.Name));

    /// <summary>
    /// The name fragment that ignores <paramref name="monitor"/> in the ignored-devices list: the graphics driver's
    /// device name when it is one (so every monitor on that card, also ones connected later, is covered), otherwise the
    /// HDMI/DisplayPort part of its name.
    /// </summary>
    public static string ExclusionFragment(AudioEndpoint monitor)
    {
        if (DisplayAudioDevice(monitor) is { } device) return device;
        if (monitor.Name.Contains(GenericHdmiFragment, StringComparison.OrdinalIgnoreCase)) return GenericHdmiFragment;
        return MonitorConnection().Match(monitor.Name) is { Success: true } m ? m.Value : monitor.DisplayDeviceName;
    }

    static string? DisplayAudioDevice(AudioEndpoint endpoint) =>
        DisplayAudioDevices.FirstOrDefault(d => endpoint.DisplayDeviceName.Equals(d, StringComparison.OrdinalIgnoreCase));
}
