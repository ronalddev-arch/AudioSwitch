using AudioSwitch.Core.Sonar;

namespace AudioSwitch.Core.Routing;

/// <summary>What AudioSwitch manages. <see cref="Sonar"/>: Sonar's outputs + mic and the Windows playback defaults.
/// <see cref="WindowsOnly"/>: only Windows' playback and recording defaults, because there is no Sonar.
/// <see cref="Undetermined"/>: Sonar isn't answering but may come back (GG starting or restarting), so nothing is
/// switched: when Sonar is in use the Windows default is usually one of its virtual devices, and moving it to a
/// physical device while Sonar merely restarts would be wrong.</summary>
public enum SwitchingMode { Undetermined, Sonar, WindowsOnly }

public sealed record SwitchingModeVerdict(SwitchingMode Mode, string Reason);

/// <summary>
/// Decides the <see cref="SwitchingMode"/> from repeated Sonar probes. Conservative: only facts lead to Windows-only
/// mode right away (GG not installed, or GG says Sonar is turned off). If GG is installed but doesn't answer, AudioSwitch
/// waits <see cref="Grace"/> of continuous silence first. While GG answers that Sonar is starting, it keeps waiting.
/// Sonar answering always means <see cref="SwitchingMode.Sonar"/>.
/// </summary>
public sealed class SwitchingModeRule(TimeSpan grace)
{
    /// <summary>Long enough for GG to start at sign-in or to restart after an update.</summary>
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromMinutes(2);

    DateTimeOffset? _notReachableSince;

    public TimeSpan Grace { get; } = grace;

    public SwitchingModeVerdict Current { get; private set; } = new(SwitchingMode.Undetermined, "not checked yet");

    /// <summary>Feeds one probe result taken at <paramref name="now"/>; returns (and remembers) the resulting mode.</summary>
    public SwitchingModeVerdict Next(SonarPresence presence, DateTimeOffset now)
    {
        if (presence != SonarPresence.NotReachable) _notReachableSince = null;
        return Current = presence switch
        {
            SonarPresence.Running => new(SwitchingMode.Sonar, "Sonar is running"),
            SonarPresence.NotInstalled => new(SwitchingMode.WindowsOnly, "GG not installed"),
            SonarPresence.Disabled => new(SwitchingMode.WindowsOnly, "Sonar turned off in GG"),
            SonarPresence.Starting => new(SwitchingMode.Undetermined, "Sonar is starting"),
            _ => NotReachable(now),
        };
    }

    SwitchingModeVerdict NotReachable(DateTimeOffset now)
    {
        var since = _notReachableSince ??= now;
        // Already Windows-only (e.g. GG closed after Sonar was turned off): stay, there is no Sonar to protect.
        return Current.Mode == SwitchingMode.WindowsOnly || now - since >= Grace
            ? new(SwitchingMode.WindowsOnly, "GG not running")
            : new(SwitchingMode.Undetermined, "GG/Sonar not reachable");
    }
}
