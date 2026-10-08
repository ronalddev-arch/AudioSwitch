using AudioSwitch.Core.Audio;
using AudioSwitch.Core.Settings;
using AudioSwitch.Core.Watching;
using static AudioSwitch.Core.Tests.TestEndpoints;

namespace AudioSwitch.Core.Tests;

public class FirstRunSetupTests
{
    static readonly AudioEndpoint BuiltInSpeakers = new("{0.0.0.00000000}.{e1}",
        "Speakers (Realtek(R) Audio)", "Realtek(R) Audio", EndpointFlow.Render, "HDAUDIO");
    static readonly AudioEndpoint VirtualCable = new("{0.0.0.00000000}.{e2}",
        "CABLE Input (VB-Audio Virtual Cable)", "VB-Audio Virtual Cable", EndpointFlow.Render, "ROOT");
    static readonly AudioEndpoint TvHdmi = new("{0.0.0.00000000}.{e3}",
        "Digital Audio (HDMI) (High Definition Audio Device)", "High Definition Audio Device", EndpointFlow.Render, "HDAUDIO");

    /// <summary>A typical PC: the Arctis (with its mic, Sonar's virtual devices), a Bluetooth speaker with its Hands-Free
    /// endpoints, a monitor on the NVIDIA card, onboard sound and a virtual cable.</summary>
    static readonly AudioEndpoint[] Pc = [.. All, BuiltInSpeakers, VirtualCable];

    static readonly AudioSwitchSettings Defaults = AudioSwitchSettings.CreateDefault();

    static FirstRunChoices Choose(bool useMonitors, params AudioEndpoint[] devices) =>
        new(devices, useMonitors, CheckForUpdates: true, KeepWindowsOnSonar: true, SonarFound: false);

    [Fact]
    public void Devices_list_headsets_and_speakers_first_then_built_in_sound_then_the_rest_and_tick_physical_ones()
    {
        var devices = FirstRunSetup.Devices(Pc, useMonitorSpeakers: false);

        Assert.Equal([Arctis, SoundCore, BuiltInSpeakers, VirtualCable], devices.Select(d => d.Output));
        Assert.Equal([true, true, true, false], devices.Select(d => d.Ticked));
        Assert.Equal(["Arctis Nova Pro Wireless", "SoundCore 2 Stereo", "Realtek(R) Audio", "VB-Audio Virtual Cable"], devices.Select(d => d.Name));
        Assert.Equal(["USB", "Bluetooth", "Built-in", "Other"], devices.Select(d => d.Connection));
    }

    [Fact]
    public void Using_monitor_speakers_lists_the_monitors_ticked_after_built_in_sound()
    {
        var devices = FirstRunSetup.Devices(Pc, useMonitorSpeakers: true);

        Assert.Equal([Arctis, SoundCore, BuiltInSpeakers, MonitorHdmi, VirtualCable], devices.Select(d => d.Output));
        var monitor = devices.Single(d => d.Output == MonitorHdmi);
        Assert.True(monitor.Ticked);
        Assert.Equal("Monitor/TV", monitor.Connection);
    }

    [Fact]
    public void Monitors_are_the_connected_monitor_and_tv_outputs_only() =>
        Assert.Equal([TvHdmi, MonitorHdmi], FirstRunSetup.Monitors([.. Pc, TvHdmi])); // by device name

    [Fact]
    public void Each_chosen_device_gets_a_profile_in_the_chosen_order()
    {
        var settings = FirstRunSetup.Build(Defaults, Choose(false, SoundCore, Arctis, BuiltInSpeakers), Pc);

        Assert.Equal(["SoundCore 2 Stereo", "Arctis Nova Pro Wireless", "Realtek(R) Audio"], settings.Profiles.Select(p => p.Name));
        // Filled in like Settings → Devices → Add: the Arctis gets its own mic, no per-brand rules.
        Assert.Equal(["Arctis Nova Pro Wireless"], settings.Profiles[1].MicPreference);
        Assert.All(settings.Profiles, p => Assert.Null(p.WindowsDefault));
        Assert.All(settings.Profiles, p => Assert.False(p.DisconnectWhenDeclined));
        Assert.Empty(settings.Validate());
    }

    /// <summary>Windows Sandbox's "Remote Audio": the endpoint has a name but no device name.</summary>
    static readonly AudioEndpoint NoDeviceName = new("{0.0.0.00000000}.{e5}", "Speakers", "", EndpointFlow.Render, "SWD");

    [Fact]
    public void A_device_without_a_device_name_gets_a_valid_profile_named_after_its_endpoint()
    {
        var settings = FirstRunSetup.Build(Defaults, Choose(false, NoDeviceName), [NoDeviceName]);

        var profile = Assert.Single(settings.Profiles);
        Assert.Equal(("Speakers", "Speakers"), (profile.Name, profile.Match));
        Assert.Empty(settings.Validate());
    }

    [Fact]
    public void A_nameless_output_is_not_offered()
    {
        var nameless = new AudioEndpoint("{0.0.0.00000000}.{e6}", " ", "", EndpointFlow.Render, "SWD");

        Assert.Equal([NoDeviceName], ProfileSuggestions.PickableOutputs([nameless, NoDeviceName]));
        Assert.Empty(FirstRunSetup.Build(Defaults, Choose(false, nameless), [nameless]).Profiles);
    }

    [Fact]
    public void Not_using_monitor_speakers_ignores_every_connected_monitor()
    {
        var settings = FirstRunSetup.Build(Defaults, Choose(false, Arctis), [.. Pc, TvHdmi]);

        Assert.Equal(["(HDMI)", "Hands-Free", "NVIDIA High Definition Audio"], settings.ExcludedNameFragments);
        Assert.Null(new Watching.DeviceFilter(settings.ToFilterOptions()).GetExclusionReason(BuiltInSpeakers));
    }

    [Fact]
    public void Using_monitor_speakers_drops_every_ignored_name_that_would_hide_them()
    {
        var withNvidia = Defaults with { ExcludedNameFragments = ["(HDMI)", "NVIDIA High Definition Audio", "Hands-Free"] };

        var settings = FirstRunSetup.Build(withNvidia, Choose(true, Arctis, MonitorHdmi), Pc);

        Assert.Equal(["Hands-Free"], settings.ExcludedNameFragments);
        Assert.Equal(["Arctis Nova Pro Wireless", "NVIDIA High Definition Audio"], settings.Profiles.Select(p => p.Name));
    }

    [Fact]
    public void Without_monitors_connected_the_ignored_devices_stay_the_defaults()
    {
        AudioEndpoint[] laptop = [SoundCore, BuiltInSpeakers];

        Assert.Equal(Defaults.ExcludedNameFragments, FirstRunSetup.Build(Defaults, Choose(false, SoundCore), laptop).ExcludedNameFragments);
        Assert.Empty(FirstRunSetup.Monitors(laptop));
    }

    [Fact]
    public void A_chosen_device_that_will_be_ignored_gets_no_profile()
    {
        var settings = FirstRunSetup.Build(Defaults, Choose(false, MonitorHdmi, Arctis), Pc);

        Assert.Equal(["Arctis Nova Pro Wireless"], settings.Profiles.Select(p => p.Name));
    }

    [Fact]
    public void With_sonar_found_and_kept_the_profiles_keep_windows_on_sonars_devices()
    {
        var settings = FirstRunSetup.Build(Defaults, Choose(false, SoundCore, Arctis) with { SonarFound = true }, Pc);

        Assert.All(settings.Profiles, p => Assert.Equal(
            (DeviceFilterOptions.SonarGamingEndpoint, DeviceFilterOptions.SonarChatEndpoint), (p.WindowsDefault, p.WindowsCommunicationsDefault)));
        Assert.True(settings.NewDevicesUseSonarDefaults);
        Assert.Empty(settings.Validate());
    }

    [Fact]
    public void With_sonar_found_but_declined_the_profiles_use_the_devices_themselves_and_the_answer_is_saved()
    {
        var settings = FirstRunSetup.Build(Defaults, Choose(false, SoundCore, Arctis) with { SonarFound = true, KeepWindowsOnSonar = false }, Pc);

        Assert.All(settings.Profiles, p => Assert.Null(p.WindowsDefault));
        Assert.False(settings.NewDevicesUseSonarDefaults);
    }

    [Fact]
    public void Without_sonar_the_profiles_use_the_devices_themselves_but_the_answer_is_still_saved()
    {
        // The question isn't shown; its default answer becomes the setting for when Sonar is installed later.
        var settings = FirstRunSetup.Build(Defaults, Choose(false, SoundCore, Arctis), Pc);

        Assert.All(settings.Profiles, p => Assert.Null(p.WindowsDefault));
        Assert.All(settings.Profiles, p => Assert.Null(p.WindowsCommunicationsDefault));
        Assert.True(settings.NewDevicesUseSonarDefaults);
    }

    [Fact]
    public void Identical_devices_share_one_profile()
    {
        var a = new AudioEndpoint("{0.0.0.00000000}.{f1}", "Speakers (USB Audio)", "USB Audio", EndpointFlow.Render, "USB");
        var b = new AudioEndpoint("{0.0.0.00000000}.{f2}", "Speakers (USB Audio)", "USB Audio", EndpointFlow.Render, "USB");

        var settings = FirstRunSetup.Build(Defaults, Choose(false, a, b), [a, b]);

        Assert.Single(settings.Profiles);
        Assert.Empty(settings.Validate());
    }

    [Fact]
    public void Only_the_chosen_options_change_the_defaults()
    {
        var settings = FirstRunSetup.Build(Defaults,
            new FirstRunChoices([], UseMonitorSpeakers: false, CheckForUpdates: false, KeepWindowsOnSonar: true, SonarFound: false), [SoundCore]);

        Assert.False(settings.CheckForUpdatesAutomatically);
        Assert.True(settings.NewDevicesUseSonarDefaults);
        Assert.Empty(settings.Profiles);
        Assert.Equal(Defaults.ExcludedNameFragments, settings.ExcludedNameFragments);
        Assert.Equal(Defaults.PopupTimeoutSeconds, settings.PopupTimeoutSeconds);
        Assert.Equal(Defaults.DefaultMicPreference, settings.DefaultMicPreference);
        Assert.Equal(AudioSwitchSettings.CurrentSchemaVersion, settings.SchemaVersion);
    }
}
