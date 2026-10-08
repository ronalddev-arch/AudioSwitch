using System.Text.Json;
using AudioSwitch.Core.Routing;
using AudioSwitch.Core.Settings;

namespace AudioSwitch.Core.Tests;

public class SettingsTests
{
    static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>settings.json as AudioSwitch 0.2.0 wrote it (no schemaVersion, no Windows default fields).</summary>
    const string V1File = """
        {
          "popupTimeoutSeconds": 15,
          "excludedNameFragments": ["(HDMI)", "NVIDIA High Definition Audio", "Hands-Free"],
          "profiles": [
            { "name": "SteelSeries Arctis", "match": "Arctis Nova Pro Wireless", "micPreference": ["Arctis Nova Pro Wireless"], "disconnectWhenDeclined": false },
            { "name": "SoundCore 2", "match": "SoundCore 2", "micPreference": ["Arctis Nova Pro Wireless", "Brio 105"], "disconnectWhenDeclined": false },
            { "name": "WH-CH520 (shared)", "match": "WH-CH520", "micPreference": ["Brio 105"], "disconnectWhenDeclined": true }
          ],
          "defaultMicPreference": ["Arctis Nova Pro Wireless", "Brio 105"]
        }
        """;

    [Fact]
    public void Migrating_a_v1_file_gives_the_headset_sonar_windows_defaults()
    {
        var v1 = JsonSerializer.Deserialize<AudioSwitchSettings>(V1File, Web)!;
        Assert.Equal(1, v1.SchemaVersion);

        var migrated = v1.Migrate();

        Assert.Equal(AudioSwitchSettings.CurrentSchemaVersion, migrated.SchemaVersion);
        var arctis = migrated.Profiles[0];
        Assert.Equal("SteelSeries Sonar - Gaming", arctis.WindowsDefault);
        Assert.Equal("SteelSeries Sonar - Chat", arctis.WindowsCommunicationsDefault);
        Assert.Null(migrated.Profiles[1].WindowsDefault);
        Assert.True(migrated.Profiles[2].DisconnectWhenDeclined);
    }

    [Fact]
    public void Migration_keeps_user_edits_to_a_profile()
    {
        var v1 = JsonSerializer.Deserialize<AudioSwitchSettings>(V1File, Web)!;
        var edited = v1 with { Profiles = [v1.Profiles[0] with { WindowsDefault = "My DAC" }, .. v1.Profiles.Skip(1)] };

        Assert.Equal("My DAC", edited.Migrate().Profiles[0].WindowsDefault);
    }

    /// <summary>settings.json as AudioSwitch 0.4.x wrote it (schema v2, no update setting), with the headset's Windows
    /// defaults cleared on purpose.</summary>
    const string V2FileWithClearedDefaults = """
        {
          "schemaVersion": 2,
          "popupTimeoutSeconds": 20,
          "excludedNameFragments": ["Hands-Free"],
          "profiles": [
            { "name": "SteelSeries Arctis", "match": "Arctis Nova Pro Wireless", "micPreference": [], "disconnectWhenDeclined": false,
              "windowsDefault": null, "windowsCommunicationsDefault": null }
          ],
          "defaultMicPreference": []
        }
        """;

    /// <summary>settings.json as a v0.4 build wrote it: every field explicit.</summary>
    const string V2File = """
        {
          "schemaVersion": 2,
          "popupTimeoutSeconds": 15,
          "excludedNameFragments": ["(HDMI)", "NVIDIA High Definition Audio", "Hands-Free"],
          "profiles": [
            { "name": "SteelSeries Arctis", "match": "Arctis Nova Pro Wireless", "micPreference": ["Arctis Nova Pro Wireless"], "disconnectWhenDeclined": false,
              "windowsDefault": "SteelSeries Sonar - Gaming", "windowsCommunicationsDefault": "SteelSeries Sonar - Chat" },
            { "name": "SoundCore 2", "match": "SoundCore 2", "micPreference": ["Arctis Nova Pro Wireless", "Brio 105"], "disconnectWhenDeclined": false,
              "windowsDefault": null, "windowsCommunicationsDefault": null },
            { "name": "WH-CH520 (shared)", "match": "WH-CH520", "micPreference": ["Brio 105"], "disconnectWhenDeclined": true,
              "windowsDefault": null, "windowsCommunicationsDefault": null }
          ],
          "defaultMicPreference": ["Arctis Nova Pro Wireless", "Brio 105"]
        }
        """;

    [Fact]
    public void Migrating_a_v2_file_turns_on_update_checks_and_keeps_everything_else()
    {
        var v2 = JsonSerializer.Deserialize<AudioSwitchSettings>(V2FileWithClearedDefaults, Web)!;

        var migrated = v2.Migrate();

        Assert.Equal(AudioSwitchSettings.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.True(migrated.CheckForUpdatesAutomatically);
        Assert.Equal(20, migrated.PopupTimeoutSeconds);
        Assert.Null(migrated.Profiles[0].WindowsDefault); // the v1 step must not refill what the user cleared
        Assert.Null(migrated.Profiles[0].WindowsCommunicationsDefault);
    }

    /// <summary>settings.json as AudioSwitch 0.5.x wrote it (schema v3, no NewDevicesUseSonarDefaults).</summary>
    const string V3File = """
        {
          "schemaVersion": 3,
          "popupTimeoutSeconds": 15,
          "checkForUpdatesAutomatically": false,
          "excludedNameFragments": ["(HDMI)", "Hands-Free"],
          "profiles": [
            { "name": "SoundCore 2", "match": "SoundCore 2", "micPreference": [], "disconnectWhenDeclined": false,
              "windowsDefault": null, "windowsCommunicationsDefault": null }
          ],
          "defaultMicPreference": []
        }
        """;

    [Theory]
    [InlineData(V1File)]
    [InlineData(V2File)]
    [InlineData(V3File)]
    public void Migrated_files_keep_new_devices_as_their_own_windows_default(string file)
    {
        var loaded = JsonSerializer.Deserialize<AudioSwitchSettings>(file, Web)!;
        Assert.True(loaded.NewDevicesUseSonarDefaults); // what a file without the field reads as

        var migrated = loaded.Migrate();

        Assert.False(migrated.NewDevicesUseSonarDefaults); // Add… behaves as before the setting existed
        Assert.Equal(AudioSwitchSettings.CurrentSchemaVersion, migrated.SchemaVersion);
    }

    [Fact]
    public void Migrating_a_v3_file_changes_nothing_else()
    {
        var v3 = JsonSerializer.Deserialize<AudioSwitchSettings>(V3File, Web)!;

        Assert.Equal(v3 with { SchemaVersion = AudioSwitchSettings.CurrentSchemaVersion, NewDevicesUseSonarDefaults = false }, v3.Migrate());
        Assert.False(v3.Migrate().CheckForUpdatesAutomatically);
    }

    [Fact]
    public void New_installs_keep_new_devices_on_sonar_and_either_answer_survives_a_round_trip()
    {
        Assert.True(AudioSwitchSettings.CreateDefault().NewDevicesUseSonarDefaults);

        foreach (var value in new[] { true, false })
        {
            var saved = AudioSwitchSettings.CreateDefault() with { NewDevicesUseSonarDefaults = value };
            var roundTripped = JsonSerializer.Deserialize<AudioSwitchSettings>(JsonSerializer.Serialize(saved, Web), Web)!.Migrate();
            Assert.Equal(value, roundTripped.NewDevicesUseSonarDefaults);
        }
    }

    [Fact]
    public void Turning_off_update_checks_survives_a_round_trip()
    {
        var off = AudioSwitchSettings.CreateDefault() with { CheckForUpdatesAutomatically = false };

        var roundTripped = JsonSerializer.Deserialize<AudioSwitchSettings>(JsonSerializer.Serialize(off, Web), Web)!;

        Assert.False(roundTripped.Migrate().CheckForUpdatesAutomatically);
    }

    [Fact]
    public void A_file_with_explicit_values_keeps_them_whatever_the_built_in_defaults_are()
    {
        var loaded = JsonSerializer.Deserialize<AudioSwitchSettings>(V2File, Web)!;

        // v3 adds no value and v4 keeps Add… giving the device itself, so nothing else changes.
        Assert.Equal(loaded with { SchemaVersion = AudioSwitchSettings.CurrentSchemaVersion, NewDevicesUseSonarDefaults = false }, loaded.Migrate());
        Assert.Equal(ExampleSettings.Sample.ExcludedNameFragments, loaded.ExcludedNameFragments);
        Assert.Equal(ExampleSettings.Sample.DefaultMicPreference, loaded.DefaultMicPreference);
        Assert.Equal(ExampleSettings.Sample.Profiles, loaded.Profiles, (a, b) => a.Name == b.Name && a.Match == b.Match
            && a.MicPreference.SequenceEqual(b.MicPreference) && a.DisconnectWhenDeclined == b.DisconnectWhenDeclined
            && a.WindowsDefault == b.WindowsDefault && a.WindowsCommunicationsDefault == b.WindowsCommunicationsDefault);
    }

    [Fact]
    public void Default_settings_name_no_devices()
    {
        var defaults = AudioSwitchSettings.CreateDefault();

        Assert.Empty(defaults.Profiles);
        Assert.Empty(defaults.DefaultMicPreference);
        Assert.Equal(["(HDMI)", "Hands-Free"], defaults.ExcludedNameFragments);
    }

    [Fact]
    public void Current_settings_need_no_migration()
    {
        var current = AudioSwitchSettings.CreateDefault();
        Assert.Same(current, current.Migrate());

        var roundTripped = JsonSerializer.Deserialize<AudioSwitchSettings>(JsonSerializer.Serialize(current, Web), Web)!;
        Assert.Equal(AudioSwitchSettings.CurrentSchemaVersion, roundTripped.SchemaVersion);
    }

    [Fact]
    public void Normalize_trims_text_and_drops_blank_entries()
    {
        var edited = AudioSwitchSettings.CreateDefault() with
        {
            ExcludedNameFragments = [" (HDMI) ", "", "(hdmi)"],
            DefaultMicPreference = ["Brio 105", "  "],
            Profiles = [new DeviceProfile { Name = " Speaker ", Match = " JBL ", MicPreference = [""], WindowsDefault = "  " }],
        };

        var normalized = edited.Normalize();

        Assert.Equal(["(HDMI)"], normalized.ExcludedNameFragments);
        Assert.Equal(["Brio 105"], normalized.DefaultMicPreference);
        var profile = Assert.Single(normalized.Profiles);
        Assert.Equal(("Speaker", "JBL"), (profile.Name, profile.Match));
        Assert.Empty(profile.MicPreference);
        Assert.Null(profile.WindowsDefault);
    }

    [Fact]
    public void Default_settings_are_valid()
    {
        Assert.Empty(AudioSwitchSettings.CreateDefault().Normalize().Validate());
    }

    [Fact]
    public void Validate_rejects_profiles_that_would_match_everything_or_share_a_name()
    {
        var settings = AudioSwitchSettings.CreateDefault() with
        {
            PopupTimeoutSeconds = 1,
            Profiles =
            [
                new DeviceProfile { Name = "Speaker", Match = "" },
                new DeviceProfile { Name = "speaker", Match = "JBL" },
                new DeviceProfile { Name = "", Match = "Sony" },
            ],
        };

        var problems = settings.Normalize().Validate();

        Assert.Contains(problems, p => p.Contains("timeout"));
        Assert.Contains(problems, p => p.Contains("'Speaker' needs a device name"));
        Assert.Contains(problems, p => p.Contains("#3 needs a name"));
        Assert.Contains(problems, p => p.Contains("More than one device is called"));
    }

    [Fact]
    public void Updated_rules_apply_to_the_next_decision()
    {
        var rules = new RoutingRules(AudioSwitchSettings.CreateDefault());
        Assert.Equal("JBL Flip Stereo", rules.DisplayName(TestEndpoints.UnknownSpeaker));

        rules.Update(AudioSwitchSettings.CreateDefault() with { Profiles = [new DeviceProfile { Name = "Party speaker", Match = "JBL" }] });

        Assert.Equal("Party speaker", rules.DisplayName(TestEndpoints.UnknownSpeaker));
        Assert.Null(rules.ProfileFor(TestEndpoints.Arctis));
    }
}
