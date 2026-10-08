using AudioSwitch.Core.Watching;

namespace AudioSwitch.Core.Settings;

/// <summary>What to do with one physical output device.</summary>
public sealed record DeviceProfile
{
    /// <summary>Display name, also the key for "recently used".</summary>
    public required string Name { get; init; }

    /// <summary>Case-insensitive fragment of the output endpoint name, e.g. "Arctis Nova Pro Wireless".</summary>
    public required string Match { get; init; }

    /// <summary>Mics to route to Sonar when switching to this device: first available wins (fragments of the mic
    /// endpoint name). A SteelSeries headset mic only counts as available while the headset is on. Empty = use
    /// <see cref="AudioSwitchSettings.DefaultMicPreference"/>.</summary>
    public IReadOnlyList<string> MicPreference { get; init; } = [];

    /// <summary>When the switch popup is declined or times out, disconnect this Bluetooth device from the PC.</summary>
    public bool DisconnectWhenDeclined { get; init; }

    /// <summary>Windows default playback device (default + multimedia roles) when switching to this device, as a
    /// fragment of an endpoint name, e.g. "SteelSeries Sonar - Gaming". Null = the device itself.</summary>
    public string? WindowsDefault { get; init; }

    /// <summary>Windows communications default (calls, Discord, Teams). Null = same as <see cref="WindowsDefault"/>.</summary>
    public string? WindowsCommunicationsDefault { get; init; }
}

public sealed record AudioSwitchSettings
{
    public const int CurrentSchemaVersion = 4;

    /// <summary>Lets newer versions fill in settings added since the file was written (see <see cref="Migrate"/>).
    /// Absent in files written before it existed, hence the default of 1; new settings come from <see cref="CreateDefault"/>.</summary>
    public int SchemaVersion { get; init; } = 1;

    public static AudioSwitchSettings CreateDefault() => new() { SchemaVersion = CurrentSchemaVersion };

    public int PopupTimeoutSeconds { get; init; } = 15;

    /// <summary>Look for a new release shortly after startup and then daily (installed builds only). Installing is
    /// always the user's choice.</summary>
    public bool CheckForUpdatesAutomatically { get; init; } = true;

    /// <summary>A profile made for a new device (first-run setup, Settings → Devices → Add…) keeps the Windows defaults
    /// on Sonar's Gaming and Chat devices, so that Sonar's EQ and mixer keep working; only while Sonar is in use
    /// (<see cref="ProfileSuggestions.SonarMayBeInUse"/>). Off: the device itself. On for new installs (the first-run
    /// setup asks); files written before it existed migrate to off, the behaviour they had.</summary>
    public bool NewDevicesUseSonarDefaults { get; init; } = true;

    public IReadOnlyList<string> ExcludedNameFragments { get; init; } = new DeviceFilterOptions().ExcludedNameFragments;

    /// <summary>Known devices; the order is also the fallback order after "most recently used". Empty by default: a
    /// device without a profile still works (popup, the device itself as Windows default, <see cref="DefaultMicPreference"/>).</summary>
    public IReadOnlyList<DeviceProfile> Profiles { get; init; } = [];

    /// <summary>Mic preference for devices without a profile (or with an empty one). Empty by default: the mic is
    /// left unchanged.</summary>
    public IReadOnlyList<string> DefaultMicPreference { get; init; } = [];

    public const int MinPopupTimeoutSeconds = 5, MaxPopupTimeoutSeconds = 300;

    public DeviceFilterOptions ToFilterOptions() => new() { ExcludedNameFragments = ExcludedNameFragments };

    /// <summary>Trims every text, drops blank list entries and turns blank optional fields into null, so that what the
    /// settings window collects can be validated and saved as is.</summary>
    public AudioSwitchSettings Normalize()
    {
        static IReadOnlyList<string> Clean(IEnumerable<string> list) =>
            list.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        static string? OrNull(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

        return this with
        {
            ExcludedNameFragments = Clean(ExcludedNameFragments),
            DefaultMicPreference = Clean(DefaultMicPreference),
            Profiles = Profiles.Select(p => p with
            {
                Name = p.Name.Trim(),
                Match = p.Match.Trim(),
                MicPreference = Clean(p.MicPreference),
                WindowsDefault = OrNull(p.WindowsDefault),
                WindowsCommunicationsDefault = OrNull(p.WindowsCommunicationsDefault),
            }).ToList(),
        };
    }

    /// <summary>Problems that would make routing misbehave (an empty Match would claim every device), as messages for
    /// the settings window. Empty when the settings are fine. Expects <see cref="Normalize"/>d settings.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (PopupTimeoutSeconds is < MinPopupTimeoutSeconds or > MaxPopupTimeoutSeconds)
            problems.Add($"The popup timeout must be between {MinPopupTimeoutSeconds} and {MaxPopupTimeoutSeconds} seconds.");
        for (var i = 0; i < Profiles.Count; i++)
        {
            var p = Profiles[i];
            var label = p.Name.Length > 0 ? $"Device '{p.Name}'" : $"Device #{i + 1}";
            if (p.Name.Length == 0) problems.Add($"{label} needs a name.");
            if (p.Match.Length == 0) problems.Add($"{label} needs a device name to match.");
        }
        foreach (var name in Profiles.Where(p => p.Name.Length > 0).GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            problems.Add($"More than one device is called '{name.Key}'.");
        return problems;
    }

    /// <summary>
    /// Upgrades settings written by an older version, keeping the user's edits. Returns the same instance when nothing
    /// changed.
    /// <list type="bullet">
    /// <item>v1 (no SchemaVersion, AudioSwitch 0.2.0) → v2: profiles gain WindowsDefault/WindowsCommunicationsDefault,
    /// filled in from the 0.2.0 default profile with the same Match (<see cref="V1DefaultProfiles"/>).</item>
    /// <item>v2 → v3: CheckForUpdatesAutomatically. A file without it already reads as true (the default), so this
    /// step only records the version.</item>
    /// <item>v3 → v4: NewDevicesUseSonarDefaults, false: Add… keeps giving new devices themselves as Windows default,
    /// as before. Only new installs start with true.</item>
    /// </list>
    /// </summary>
    public AudioSwitchSettings Migrate()
    {
        if (SchemaVersion >= CurrentSchemaVersion) return this;

        var migrated = this;
        if (SchemaVersion < 2)
        {
            // Only for v1: a v2 user may have cleared these on purpose.
            var defaults = V1DefaultProfiles;
            var profiles = Profiles.Select(p =>
                defaults.FirstOrDefault(d => d.Match.Equals(p.Match, StringComparison.OrdinalIgnoreCase)) is { } d
                && p.WindowsDefault is null && p.WindowsCommunicationsDefault is null
                    ? p with { WindowsDefault = d.WindowsDefault, WindowsCommunicationsDefault = d.WindowsCommunicationsDefault }
                    : p).ToList();
            migrated = migrated with { Profiles = profiles };
        }
        if (SchemaVersion < 4)
            migrated = migrated with { NewDevicesUseSonarDefaults = false }; // a v3 file can't have it yet
        return migrated with { SchemaVersion = CurrentSchemaVersion };
    }

    /// <summary>The Windows defaults v2 gave the profiles AudioSwitch 0.2.0 shipped as defaults; frozen, because the
    /// current defaults have no profiles. Only the SteelSeries headset's had any (Sonar's Gaming/Chat devices).</summary>
    static readonly IReadOnlyList<DeviceProfile> V1DefaultProfiles =
    [
        new()
        {
            Name = "SteelSeries Arctis",
            Match = "Arctis Nova Pro Wireless",
            WindowsDefault = DeviceFilterOptions.SonarGamingEndpoint,
            WindowsCommunicationsDefault = DeviceFilterOptions.SonarChatEndpoint,
        },
    ];
}
