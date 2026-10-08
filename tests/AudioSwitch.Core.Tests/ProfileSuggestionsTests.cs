using AudioSwitch.Core.Audio;
using AudioSwitch.Core.Routing;
using AudioSwitch.Core.Settings;
using static AudioSwitch.Core.Tests.TestEndpoints;

namespace AudioSwitch.Core.Tests;

public class ProfileSuggestionsTests
{
    static readonly Guid SoundCoreContainer = new("b2c00000-0000-4000-8000-000000000000");
    static readonly Guid LocalMachine = new("00000000-0000-0000-ffff-ffffffffffff");

    [Fact]
    public void Picker_skips_sonar_virtual_devices_hands_free_endpoints_and_mics()
    {
        var outputs = ProfileSuggestions.PickableOutputs([.. All, SonarChat, WhCh520]);

        Assert.Equal([Arctis, MonitorHdmi, SoundCore, WhCh520], outputs); // sorted by device name, "8- " ignored
    }

    [Fact]
    public void Profile_is_named_and_matched_by_the_device_name_without_the_usb_prefix()
    {
        var profile = ProfileSuggestions.FromEndpoint(Arctis, All);

        Assert.Equal("Arctis Nova Pro Wireless", profile.Name);
        Assert.Equal("Arctis Nova Pro Wireless", profile.Match);
        Assert.Contains(profile.Match, Arctis.Name, StringComparison.OrdinalIgnoreCase);
    }

    static readonly AudioEndpoint SecondMonitor = new("{0.0.0.00000000}.{c3}",
        "DELL U2723QE (NVIDIA High Definition Audio)", "NVIDIA High Definition Audio", EndpointFlow.Render, "HDAUDIO");

    [Fact]
    public void Outputs_sharing_a_device_name_are_named_by_their_endpoint_label()
    {
        var profile = ProfileSuggestions.FromEndpoint(MonitorHdmi, [.. All, SecondMonitor]);

        Assert.Equal("32G2WG8", profile.Name);
        Assert.Equal("32G2WG8", profile.Match);
        Assert.DoesNotContain(profile.Match, SecondMonitor.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("DELL U2723QE", ProfileSuggestions.FriendlyName(SecondMonitor, [.. All, SecondMonitor]));
    }

    [Fact]
    public void Single_output_of_a_device_keeps_the_device_name()
    {
        Assert.Equal("NVIDIA High Definition Audio", ProfileSuggestions.FriendlyName(MonitorHdmi, All));
        // Sonar's Gaming and Chat share a device name, but that doesn't affect the Arctis.
        Assert.Equal("Arctis Nova Pro Wireless", ProfileSuggestions.FriendlyName(Arctis, [.. All, SonarChat]));
    }

    [Fact]
    public void Shared_device_name_without_a_unique_label_falls_back_to_the_device_name()
    {
        var a = new AudioEndpoint("{0.0.0.00000000}.{d1}", "Speakers (USB Audio)", "USB Audio", EndpointFlow.Render, "USB");
        var b = new AudioEndpoint("{0.0.0.00000000}.{d2}", "Speakers (USB Audio)", "USB Audio", EndpointFlow.Render, "USB");

        Assert.Equal("USB Audio", ProfileSuggestions.FriendlyName(a, [a, b]));
    }

    [Fact]
    public void Profile_uses_the_devices_own_microphone_by_name()
    {
        var profile = ProfileSuggestions.FromEndpoint(Arctis, All);

        Assert.Equal(["Arctis Nova Pro Wireless"], profile.MicPreference);
        Assert.Contains(profile.MicPreference[0], ArctisMic.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Hands_free_microphone_does_not_count_as_the_devices_own()
    {
        var output = SoundCore with { ContainerId = SoundCoreContainer };
        var handsFreeMic = SoundCoreHandsFreeMic with { ContainerId = SoundCoreContainer };

        var profile = ProfileSuggestions.FromEndpoint(output, [output, handsFreeMic, Brio]);

        Assert.Empty(profile.MicPreference); // = the default microphone order
    }

    [Fact]
    public void Microphone_with_the_same_container_id_counts_even_when_its_name_differs()
    {
        var output = SoundCore with { ContainerId = SoundCoreContainer };
        var mic = new AudioEndpoint("{0.0.1.00000000}.{a1}", "Microphone (SoundCore 2 Mic)", "SoundCore 2 Mic", EndpointFlow.Capture, "BTHENUM", SoundCoreContainer);

        Assert.Equal(["SoundCore 2 Mic"], ProfileSuggestions.FromEndpoint(output, [output, mic, Brio]).MicPreference);
    }

    [Fact]
    public void Different_container_ids_win_over_equal_names()
    {
        var output = Arctis with { ContainerId = Guid.NewGuid() };
        var mic = ArctisMic with { ContainerId = Guid.NewGuid() };

        Assert.Null(ProfileSuggestions.OwnMicrophone(output, [output, mic]));
    }

    [Fact]
    public void The_pcs_own_container_does_not_tie_built_in_devices_together()
    {
        // Windows gives every built-in device the same "local machine" container.
        var hdmi = MonitorHdmi with { ContainerId = LocalMachine };
        var onboardMic = new AudioEndpoint("{0.0.1.00000000}.{b2}", "Microphone (Realtek(R) Audio)", "Realtek(R) Audio", EndpointFlow.Capture, "HDAUDIO", LocalMachine);

        Assert.Null(ProfileSuggestions.OwnMicrophone(hdmi, [hdmi, onboardMic]));
    }

    [Fact]
    public void Device_without_a_microphone_gets_the_default_order_and_no_invented_rules()
    {
        var profile = ProfileSuggestions.FromEndpoint(WhCh520, [.. All, WhCh520]);

        Assert.Equal("WH-CH520 Stereo", profile.Name);
        Assert.Empty(profile.MicPreference);
        Assert.Null(profile.WindowsDefault);
        Assert.Null(profile.WindowsCommunicationsDefault);
        Assert.False(profile.DisconnectWhenDeclined);
    }

    [Fact]
    public void With_sonar_defaults_the_profile_keeps_windows_on_sonars_gaming_and_chat_devices()
    {
        var profile = ProfileSuggestions.FromEndpoint(SoundCore, All, sonarDefaults: true);

        Assert.Equal("SteelSeries Sonar - Gaming", profile.WindowsDefault);
        Assert.Equal("SteelSeries Sonar - Chat", profile.WindowsCommunicationsDefault);
        Assert.Contains(profile.WindowsDefault!, SonarGaming.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(profile.WindowsCommunicationsDefault!, SonarChat.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("SoundCore 2 Stereo", profile.Name); // the rest is unchanged
        Assert.False(profile.DisconnectWhenDeclined);
    }

    [Theory]
    [InlineData(true, SwitchingMode.Sonar, true)]
    [InlineData(true, SwitchingMode.Undetermined, true)] // GG installed, Sonar not answering yet
    [InlineData(true, SwitchingMode.WindowsOnly, false)]
    [InlineData(true, null, false)]
    [InlineData(false, SwitchingMode.Sonar, false)]
    [InlineData(false, SwitchingMode.Undetermined, false)]
    public void Sonar_defaults_need_the_setting_and_sonar(bool setting, SwitchingMode? mode, bool expected) =>
        Assert.Equal(expected, ProfileSuggestions.UseSonarDefaults(setting, mode));

    [Fact]
    public void Matching_profile_uses_the_routing_rule_and_ignores_blank_matches()
    {
        var blank = new DeviceProfile { Name = "New", Match = " " };
        var soundCore = new DeviceProfile { Name = "SoundCore 2", Match = "soundcore 2" };

        Assert.Same(soundCore, ProfileSuggestions.MatchingProfile(SoundCore, [blank, soundCore]));
        Assert.Null(ProfileSuggestions.MatchingProfile(Arctis, [blank, soundCore]));
    }

    [Theory]
    [InlineData("BTHENUM", "Bluetooth")]
    [InlineData("USB", "USB")]
    [InlineData("HDAUDIO", "Built-in/HDMI")]
    [InlineData("hdaudio", "Built-in/HDMI")]
    [InlineData("ROOT", "ROOT")]
    public void Connection_type_comes_from_the_bus(string bus, string expected) =>
        Assert.Equal(expected, ProfileSuggestions.ConnectionType(bus));

    [Fact]
    public void Connection_type_tells_monitor_audio_from_built_in_sound()
    {
        var onboard = new AudioEndpoint("{0.0.0.00000000}.{b3}", "Speakers (Realtek(R) Audio)", "Realtek(R) Audio", EndpointFlow.Render, "HDAUDIO");

        Assert.Equal("Monitor/TV", ProfileSuggestions.ConnectionType(MonitorHdmi));
        Assert.Equal("Built-in", ProfileSuggestions.ConnectionType(onboard));
        Assert.Equal("Bluetooth", ProfileSuggestions.ConnectionType(SoundCore));
    }
}
