namespace AudioSwitch.Core.Sonar;

/// <summary>Sonar's mixer mode. In <see cref="Classic"/> mode every output channel has its own physical device; in
/// <see cref="Stream"/> (streamer) mode the channels are mixed into a personal mix and a stream mix, each with one
/// device. <see cref="Unknown"/>: a mode this build doesn't know, managed like classic.</summary>
public enum SonarMode { Classic, Stream, Unknown }

/// <summary>What Sonar can redirect to a physical device. <see cref="Game"/>..<see cref="Aux"/> are classic-mode
/// channels; <see cref="Monitoring"/> is stream mode's personal mix (what the user hears, all channels together).
/// <see cref="Mic"/> exists in both modes, with a separate device per mode.</summary>
public enum SonarChannel { Game, Chat, Media, Aux, Mic, Monitoring }

public static class SonarChannels
{
    public static readonly IReadOnlyList<SonarChannel> ClassicOutputs = [SonarChannel.Game, SonarChannel.Chat, SonarChannel.Media, SonarChannel.Aux];

    /// <summary>Stream mode: only the personal mix. The stream mix (what the audience hears) is never touched.</summary>
    public static readonly IReadOnlyList<SonarChannel> StreamOutputs = [SonarChannel.Monitoring];

    /// <summary>The output channels that decide what the user hears in <paramref name="mode"/>; the first one is the
    /// "main" output (Game in classic mode).</summary>
    public static IReadOnlyList<SonarChannel> OutputsFor(SonarMode mode) => mode == SonarMode.Stream ? StreamOutputs : ClassicOutputs;

    /// <summary>Output channels chosen for one mode, translated to <paramref name="mode"/> (a plan made before Sonar
    /// switched modes): any classic output means the personal mix, and the personal mix means every classic output.</summary>
    public static IReadOnlyList<SonarChannel> ForMode(IReadOnlyList<SonarChannel> channels, SonarMode mode)
    {
        var outputs = channels.Where(c => c != SonarChannel.Mic).ToList();
        if (mode == SonarMode.Stream)
            return outputs.Count > 0 ? StreamOutputs : [];
        return outputs.SelectMany(c => c == SonarChannel.Monitoring ? ClassicOutputs : [c]).Distinct().ToList();
    }

    /// <summary>The set of output checkboxes that fits <paramref name="channels"/> (a plan's channels).</summary>
    public static IReadOnlyList<SonarChannel> OutputSetOf(IReadOnlyList<SonarChannel> channels) =>
        channels.Contains(SonarChannel.Monitoring) ? StreamOutputs : ClassicOutputs;

    public static string DisplayName(SonarChannel channel) =>
        channel == SonarChannel.Monitoring ? "Personal mix (what you hear)" : channel.ToString();
}
