using AudioSwitch.Core.Audio;
using AudioSwitch.Core.Routing;
using AudioSwitch.Core.Settings;
using AudioSwitch.Core.Sonar;
using AudioSwitch.Core.Watching;
using static AudioSwitch.Core.Tests.TestEndpoints;

namespace AudioSwitch.Core.Tests;

/// <summary>The switching rules, one scenario per test, played out with the example configuration
/// (<see cref="ExampleSettings.Sample"/>) and, at the end, with the built-in defaults.</summary>
public class RoutingEngineTests
{
    static readonly AudioSwitchSettings Settings = ExampleSettings.Sample;
    static readonly RoutingEngine Engine = new(new RoutingRules(Settings));
    static readonly DeviceFilter Filter = new(Settings.ToFilterOptions());

    /// <summary>Usable devices: the Arctis pair (if the headset is on) + Brio + the given outputs; the monitor and
    /// hands-free endpoints are always present to prove they're never picked, Sonar's virtual devices to be the
    /// Arctis' Windows defaults.</summary>
    static AvailabilitySnapshot Devices(bool headsetOn, params AudioEndpoint[] outputs) =>
        AvailabilitySnapshot.Build(
            new[] { Arctis, ArctisMic, Brio, MonitorHdmi, SoundCoreHandsFree, SonarGaming, SonarChat }.Concat(outputs),
            new Dictionary<string, bool> { [Arctis.Id] = headsetOn, [ArctisMic.Id] = headsetOn },
            Filter);

    /// <summary>Routing as AudioSwitch leaves it: Sonar to the device, Windows default to Sonar - Gaming for the Arctis
    /// and to the device itself otherwise.</summary>
    static CurrentRouting RoutedTo(AudioEndpoint output, AudioEndpoint mic) => new(
        new Dictionary<SonarChannel, string>
        {
            [SonarChannel.Game] = output.Id, [SonarChannel.Chat] = output.Id, [SonarChannel.Media] = output.Id,
            [SonarChannel.Aux] = output.Id, [SonarChannel.Mic] = mic.Id,
        },
        output == Arctis ? SonarGaming.Id : output.Id);

    static IReadOnlyList<RoutingDecision> Decide(AvailabilitySnapshot before, AvailabilitySnapshot after, CurrentRouting current,
        params string[] recent) => Engine.Decide(after.DiffFrom(before), after, current, recent);

    static void AssertFullSwitch(SwitchPlan plan, AudioEndpoint output, AudioEndpoint? mic, SonarMode mode = SonarMode.Classic)
    {
        Assert.Equal(output, plan.Output);
        var expectedWindows = output == Arctis ? new WindowsDefaults(SonarGaming, SonarChat) : new WindowsDefaults(output, output);
        Assert.Equal(expectedWindows, plan.WindowsDefaults);
        Assert.Equal(SonarChannels.OutputsFor(mode), plan.Channels);
        Assert.Equal(mic, plan.Mic);
    }

    // --- Popups

    [Fact]
    public void Headset_powered_on_while_on_bluetooth_asks_to_switch_audio_and_mic_to_headset()
    {
        var decision = Assert.Single(Decide(Devices(false, SoundCore), Devices(true, SoundCore), RoutedTo(SoundCore, Brio)));

        var prompt = Assert.IsType<PromptDecision>(decision);
        AssertFullSwitch(prompt.Plan, Arctis, ArctisMic);
        Assert.Contains("headset powered on", prompt.Reason);
        Assert.False(prompt.DisconnectWhenDeclined);
    }

    [Fact]
    public void Connecting_device_gets_the_first_usable_mic_of_its_preference()
    {
        var prompt = Assert.IsType<PromptDecision>(Assert.Single(
            Decide(Devices(true), Devices(true, SoundCore), RoutedTo(Arctis, ArctisMic))));

        AssertFullSwitch(prompt.Plan, SoundCore, ArctisMic);
    }

    [Fact]
    public void Connecting_device_skips_the_headset_mic_while_the_headset_is_off()
    {
        var prompt = Assert.IsType<PromptDecision>(Assert.Single(
            Decide(Devices(false), Devices(false, SoundCore), RoutedTo(Arctis, ArctisMic))));

        AssertFullSwitch(prompt.Plan, SoundCore, Brio);
    }

    [Fact]
    public void Disconnect_when_declined_device_uses_its_own_mic_preference_and_is_flagged_for_disconnect()
    {
        var prompt = Assert.IsType<PromptDecision>(Assert.Single(
            Decide(Devices(true), Devices(true, WhCh520), RoutedTo(Arctis, ArctisMic))));

        AssertFullSwitch(prompt.Plan, WhCh520, Brio);
        Assert.True(prompt.DisconnectWhenDeclined);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Device_without_a_profile_uses_the_default_mic_preference(bool headsetOn)
    {
        var prompt = Assert.IsType<PromptDecision>(Assert.Single(
            Decide(Devices(headsetOn), Devices(headsetOn, UnknownSpeaker), RoutedTo(Arctis, ArctisMic))));

        AssertFullSwitch(prompt.Plan, UnknownSpeaker, headsetOn ? ArctisMic : Brio);
        Assert.False(prompt.DisconnectWhenDeclined);
    }

    [Fact]
    public void No_popup_when_everything_already_goes_to_the_arriving_device()
    {
        var decision = Assert.Single(Decide(Devices(false, SoundCore), Devices(true, SoundCore), RoutedTo(Arctis, ArctisMic)));

        Assert.IsType<NoActionDecision>(decision);
    }

    [Fact]
    public void Headset_powered_on_still_asks_when_only_the_mic_is_elsewhere()
    {
        // Headset was off with nothing else to fall back to: outputs stayed on the Arctis, the mic moved to the Brio.
        var prompt = Assert.IsType<PromptDecision>(Assert.Single(
            Decide(Devices(false), Devices(true), RoutedTo(Arctis, Brio))));

        AssertFullSwitch(prompt.Plan, Arctis, ArctisMic);
    }

    [Fact]
    public void Profile_windows_defaults_fall_back_to_the_device_when_not_found()
    {
        var withoutSonar = AvailabilitySnapshot.Build([Arctis, ArctisMic, SoundCore],
            new Dictionary<string, bool> { [Arctis.Id] = true }, Filter);

        var plan = new RoutingRules(Settings).PlanSwitchTo(Arctis, withoutSonar, "test");

        Assert.Equal(new WindowsDefaults(Arctis, Arctis), plan.WindowsDefaults);
        Assert.Contains("not found", plan.Reason);
    }

    [Fact]
    public void Mics_arriving_never_cause_a_popup() =>
        Assert.Empty(Engine.Decide(
            [new DeviceChange(DeviceChangeKind.Arrived, Brio, "connected")], Devices(true), RoutedTo(Arctis, ArctisMic), []));

    // --- Automatic fallback

    [Fact]
    public void Headset_off_while_in_use_falls_back_to_another_output_and_its_mic()
    {
        var auto = Assert.IsType<AutoSwitchDecision>(Assert.Single(
            Decide(Devices(true, SoundCore), Devices(false, SoundCore), RoutedTo(Arctis, ArctisMic))));

        AssertFullSwitch(auto.Plan, SoundCore, Brio);
        Assert.Contains("headset powered off", auto.Reason);
    }

    [Fact]
    public void Bluetooth_off_while_in_use_falls_back_to_the_headset_with_its_profile_defaults()
    {
        var auto = Assert.IsType<AutoSwitchDecision>(Assert.Single(
            Decide(Devices(true, SoundCore), Devices(true), RoutedTo(SoundCore, ArctisMic))));

        AssertFullSwitch(auto.Plan, Arctis, ArctisMic);
    }

    /// <summary>Routing after Sonar reacted to a device vanishing from Windows: channels cleared, Windows default
    /// moved on by itself (as seen live).</summary>
    static CurrentRouting ClearedBySonar(AudioEndpoint mic) => new(
        new Dictionary<SonarChannel, string>
        {
            [SonarChannel.Game] = "", [SonarChannel.Chat] = "", [SonarChannel.Media] = "", [SonarChannel.Aux] = "",
            [SonarChannel.Mic] = mic.Id,
        },
        SonarGaming.Id);

    [Fact]
    public void Bluetooth_off_while_in_use_falls_back_even_though_sonar_already_cleared_its_routing()
    {
        var auto = Assert.IsType<AutoSwitchDecision>(Assert.Single(
            Decide(Devices(true, SoundCore), Devices(true), ClearedBySonar(ArctisMic))));

        AssertFullSwitch(auto.Plan, Arctis, ArctisMic);
        Assert.Equal("SoundCore 2 disconnected", auto.Cause);
    }

    [Fact]
    public void Mic_cleared_by_sonar_is_replaced()
    {
        var mics = new Dictionary<SonarChannel, string>(RoutedTo(SoundCore, Brio).Sonar) { [SonarChannel.Mic] = "" };
        var current = RoutedTo(SoundCore, Brio) with { Sonar = mics };

        var auto = Assert.IsType<AutoSwitchDecision>(Assert.Single(
            Decide(Devices(true, SoundCore, BrioAsBluetoothMic), Devices(true, SoundCore), current)));

        Assert.Null(auto.Plan.Output);
        Assert.Equal(ArctisMic, auto.Plan.Mic);
    }

    /// <summary>A second mic that can disappear without the Brio going away (stands in for any vanishing mic).</summary>
    static readonly AudioEndpoint BrioAsBluetoothMic = Brio with { Id = "{0.0.1.00000000}.{bt-mic}", Name = "Headset Mic (Some BT)" };

    [Fact]
    public void Unknown_sonar_routing_never_triggers_a_fallback() =>
        Assert.Empty(Decide(Devices(true, SoundCore), Devices(true), CurrentRouting.Unknown));

    [Fact]
    public void Fallback_prefers_the_most_recently_used_device()
    {
        var auto = Assert.IsType<AutoSwitchDecision>(Assert.Single(Decide(
            Devices(true, SoundCore, WhCh520), Devices(false, SoundCore, WhCh520), RoutedTo(Arctis, ArctisMic),
            recent: ["SteelSeries Arctis", "WH-CH520 (shared)", "SoundCore 2"])));

        Assert.Equal(WhCh520, auto.Plan.Output);
        Assert.Contains("most recently used", auto.Reason);
    }

    [Fact]
    public void Excluded_output_is_never_a_fallback_but_the_mic_still_moves()
    {
        var decisions = Decide(Devices(true), Devices(false), RoutedTo(Arctis, ArctisMic));

        Assert.Equal(2, decisions.Count);
        Assert.IsType<NoActionDecision>(decisions[0]); // outputs: only the monitor is left → leave routing alone
        var mic = Assert.IsType<AutoSwitchDecision>(decisions[1]);
        Assert.Null(mic.Plan.Output);
        Assert.Equal(Brio, mic.Plan.Mic);
    }

    [Fact]
    public void A_device_that_is_not_in_use_going_away_changes_nothing() =>
        Assert.Empty(Decide(Devices(true, SoundCore), Devices(true), RoutedTo(Arctis, ArctisMic)));

    [Fact]
    public void Headset_off_while_only_its_mic_is_in_use_moves_only_the_mic()
    {
        var auto = Assert.IsType<AutoSwitchDecision>(Assert.Single(
            Decide(Devices(true, SoundCore), Devices(false, SoundCore), RoutedTo(SoundCore, ArctisMic))));

        Assert.Null(auto.Plan.Output);
        Assert.Equal(Brio, auto.Plan.Mic);
    }

    [Fact]
    public void Lost_mic_with_no_alternative_changes_nothing()
    {
        var before = AvailabilitySnapshot.Build([SoundCore, Brio], null, Filter);
        var after = AvailabilitySnapshot.Build([SoundCore], null, Filter);

        Assert.IsType<NoActionDecision>(Assert.Single(Engine.Decide(after.DiffFrom(before), after, RoutedTo(SoundCore, Brio), [])));
    }

    // --- Startup

    [Fact]
    public void Startup_repairs_routing_that_points_at_a_headset_that_is_off()
    {
        var auto = Assert.IsType<AutoSwitchDecision>(Assert.Single(
            Engine.DecideAtStartup(Devices(false, SoundCore), RoutedTo(Arctis, ArctisMic), [])));

        AssertFullSwitch(auto.Plan, SoundCore, Brio);
    }

    [Fact]
    public void Startup_leaves_valid_routing_alone() =>
        Assert.Empty(Engine.DecideAtStartup(Devices(true, SoundCore), RoutedTo(Arctis, ArctisMic), []));

    [Fact]
    public void Startup_disconnects_a_disconnect_when_declined_device_that_is_not_in_use()
    {
        var disconnect = Assert.IsType<DisconnectDecision>(Assert.Single(
            Engine.DecideAtStartup(Devices(true, WhCh520), RoutedTo(Arctis, ArctisMic), [])));

        Assert.Equal(WhCh520, disconnect.Device);
    }

    [Fact]
    public void Startup_keeps_a_disconnect_when_declined_device_that_is_in_use() =>
        Assert.Empty(Engine.DecideAtStartup(Devices(true, WhCh520), RoutedTo(WhCh520, Brio), []));

    [Fact]
    public void Startup_repair_with_headset_off_falls_back_and_disconnects_the_declined_device()
    {
        // A logged situation: headset off, SoundCore + WH-CH520 connected.
        var decisions = Engine.DecideAtStartup(Devices(false, SoundCore, WhCh520), RoutedTo(Arctis, ArctisMic), []);

        Assert.Equal(2, decisions.Count);
        AssertFullSwitch(Assert.IsType<AutoSwitchDecision>(decisions[0]).Plan, SoundCore, Brio);
        Assert.Equal(WhCh520, Assert.IsType<DisconnectDecision>(decisions[1]).Device);
    }

    [Fact]
    public void Startup_does_not_disconnect_a_disconnect_when_declined_device_that_is_the_only_fallback()
    {
        var auto = Assert.IsType<AutoSwitchDecision>(Assert.Single(
            Engine.DecideAtStartup(Devices(false, WhCh520), RoutedTo(Arctis, ArctisMic), [])));

        AssertFullSwitch(auto.Plan, WhCh520, Brio);
    }

    // --- Stream mode: one device for the personal mix (what the user hears) instead of four channels; the stream mix
    // (what the audience hears) is never touched. Same rules, same Windows defaults.

    /// <summary>Stream-mode routing as AudioSwitch leaves it: the personal mix and the stream-mode mic.</summary>
    static CurrentRouting StreamRoutedTo(AudioEndpoint output, AudioEndpoint mic) => new(
        new Dictionary<SonarChannel, string> { [SonarChannel.Monitoring] = output.Id, [SonarChannel.Mic] = mic.Id },
        output == Arctis ? SonarGaming.Id : output.Id, Mode: SonarMode.Stream);

    /// <summary>Stream mode as it reads before it was ever set up (streamRedirections.classic-mode.json), or after
    /// Sonar cleared the personal mix's device.</summary>
    static CurrentRouting StreamCleared(string micId = "") => new(
        new Dictionary<SonarChannel, string> { [SonarChannel.Monitoring] = "", [SonarChannel.Mic] = micId },
        SonarGaming.Id, Mode: SonarMode.Stream);

    [Fact]
    public void Stream_mode_headset_powered_on_asks_to_switch_the_personal_mix()
    {
        var prompt = Assert.IsType<PromptDecision>(Assert.Single(
            Decide(Devices(false, SoundCore), Devices(true, SoundCore), StreamRoutedTo(SoundCore, Brio))));

        AssertFullSwitch(prompt.Plan, Arctis, ArctisMic, SonarMode.Stream);
    }

    [Fact]
    public void Stream_mode_no_popup_when_the_personal_mix_already_goes_to_the_arriving_device() =>
        Assert.IsType<NoActionDecision>(Assert.Single(
            Decide(Devices(false, SoundCore), Devices(true, SoundCore), StreamRoutedTo(Arctis, ArctisMic))));

    [Fact]
    public void Stream_mode_headset_off_falls_back_with_the_personal_mix()
    {
        var auto = Assert.IsType<AutoSwitchDecision>(Assert.Single(
            Decide(Devices(true, SoundCore), Devices(false, SoundCore), StreamRoutedTo(Arctis, ArctisMic))));

        AssertFullSwitch(auto.Plan, SoundCore, Brio, SonarMode.Stream);
    }

    [Fact]
    public void Stream_mode_bluetooth_off_falls_back_even_though_sonar_cleared_the_personal_mix()
    {
        var auto = Assert.IsType<AutoSwitchDecision>(Assert.Single(
            Decide(Devices(true, SoundCore), Devices(true), StreamCleared(ArctisMic.Id))));

        AssertFullSwitch(auto.Plan, Arctis, ArctisMic, SonarMode.Stream);
    }

    [Fact]
    public void Stream_mode_a_device_that_is_not_in_use_going_away_changes_nothing() =>
        Assert.Empty(Decide(Devices(true, SoundCore), Devices(true), StreamRoutedTo(Arctis, ArctisMic)));

    [Fact]
    public void Stream_mode_lost_mic_is_picked_for_the_device_on_the_personal_mix()
    {
        var auto = Assert.IsType<AutoSwitchDecision>(Assert.Single(
            Decide(Devices(true, SoundCore), Devices(false, SoundCore), StreamRoutedTo(SoundCore, ArctisMic))));

        Assert.Null(auto.Plan.Output);
        Assert.Equal(Brio, auto.Plan.Mic); // the SoundCore profile's choice while the headset is off
    }

    [Fact]
    public void Stream_mode_startup_sets_up_a_personal_mix_that_has_no_device()
    {
        var auto = Assert.IsType<AutoSwitchDecision>(Assert.Single(
            Engine.DecideAtStartup(Devices(true, SoundCore), StreamCleared(), ["SoundCore 2"])));

        AssertFullSwitch(auto.Plan, SoundCore, ArctisMic, SonarMode.Stream);
    }

    [Fact]
    public void A_classic_plan_already_matches_stream_routing_to_the_same_device()
    {
        var classicPlan = new RoutingRules(Settings).PlanSwitchTo(SoundCore, Devices(true, SoundCore), "test");

        Assert.True(StreamRoutedTo(SoundCore, ArctisMic).AlreadyMatches(classicPlan));
        Assert.False(StreamRoutedTo(Arctis, ArctisMic).AlreadyMatches(classicPlan));
    }

    // --- Sonar switched modes while running

    [Fact]
    public void Switching_to_stream_mode_that_was_never_set_up_repairs_it_without_disconnecting_anything()
    {
        var auto = Assert.IsType<AutoSwitchDecision>(Assert.Single(
            Engine.DecideAfterModeChange(Devices(true, WhCh520), StreamCleared(), ["SteelSeries Arctis"])));

        AssertFullSwitch(auto.Plan, Arctis, ArctisMic, SonarMode.Stream);
        Assert.Contains("after switching to stream mode", auto.Reason);
    }

    [Fact]
    public void Switching_modes_leaves_usable_routing_alone_and_disconnects_nothing() =>
        Assert.Empty(Engine.DecideAfterModeChange(Devices(true, WhCh520), StreamRoutedTo(Arctis, ArctisMic), []));

    [Fact]
    public void Switching_back_to_classic_mode_repairs_channels_that_point_at_a_device_that_is_gone()
    {
        var auto = Assert.IsType<AutoSwitchDecision>(Assert.Single(
            Engine.DecideAfterModeChange(Devices(false, SoundCore), RoutedTo(Arctis, ArctisMic), [])));

        AssertFullSwitch(auto.Plan, SoundCore, Brio);
        Assert.Contains("after switching to classic mode", auto.Reason);
    }

    // --- Windows-only mode: no Sonar, so the Windows default playback and recording devices are the routing.

    /// <summary>Devices without Sonar: nobody reports headset on/off, so the Arctis counts as usable. Sonar's virtual
    /// devices are still there (GG installed but not running) to prove they are never used. <paramref name="output"/>
    /// and <paramref name="mic"/> are the Windows defaults when the snapshot was taken.</summary>
    static AvailabilitySnapshot WindowsDevices(AudioEndpoint output, AudioEndpoint mic, params AudioEndpoint[] extra) =>
        AvailabilitySnapshot.Build(new[] { Brio, MonitorHdmi, SoundCoreHandsFree, SonarGaming, SonarChat }.Concat(extra), null, Filter)
            with { DefaultOutputId = output.Id, DefaultMicId = mic.Id };

    /// <summary>Windows-only routing now (Windows may have moved its defaults already), with the snapshot from before
    /// the change telling what was in use.</summary>
    static CurrentRouting WindowsRoutedTo(AudioEndpoint output, AudioEndpoint mic, AvailabilitySnapshot? before = null) =>
        CurrentRouting.FromWindows(output.Id, mic.Id, before);

    static void AssertWindowsSwitch(SwitchPlan plan, AudioEndpoint output, AudioEndpoint? mic)
    {
        Assert.True(plan.WindowsOnly);
        Assert.Equal(output, plan.Output);
        Assert.Equal(new WindowsDefaults(output, output), plan.WindowsDefaults);
        Assert.Empty(plan.Channels);
        Assert.Equal(mic, plan.Mic);
    }

    [Fact]
    public void Windows_only_connecting_device_asks_for_the_windows_defaults_and_the_recording_device()
    {
        var before = WindowsDevices(Arctis, ArctisMic, Arctis, ArctisMic);
        var after = WindowsDevices(Arctis, ArctisMic, Arctis, ArctisMic, WhCh520);

        var prompt = Assert.IsType<PromptDecision>(Assert.Single(
            Engine.Decide(after.DiffFrom(before), after, WindowsRoutedTo(Arctis, ArctisMic, before), [])));

        AssertWindowsSwitch(prompt.Plan, WhCh520, Brio);
        Assert.True(prompt.DisconnectWhenDeclined); // profiles work the same without Sonar
    }

    [Fact]
    public void Windows_only_a_profile_pointing_at_a_sonar_device_uses_the_device_itself()
    {
        var before = WindowsDevices(SoundCore, Brio, SoundCore);
        var after = WindowsDevices(SoundCore, Brio, SoundCore, Arctis, ArctisMic);

        var prompt = Assert.IsType<PromptDecision>(Assert.Single(
            Engine.Decide(after.DiffFrom(before), after, WindowsRoutedTo(SoundCore, Brio, before), [])));

        AssertWindowsSwitch(prompt.Plan, Arctis, ArctisMic); // not Sonar - Gaming / Sonar - Chat
        Assert.Contains("is a Sonar device and Sonar isn't running", prompt.Plan.Reason);
    }

    [Fact]
    public void Windows_only_a_device_added_with_sonar_defaults_uses_the_device_itself()
    {
        // Settings → Add… (or the first-run setup) made the profile while Sonar was in use; now Sonar is gone.
        var profile = ProfileSuggestions.FromEndpoint(UnknownSpeaker, [UnknownSpeaker, SonarGaming, SonarChat], sonarDefaults: true);
        var rules = new RoutingRules(AudioSwitchSettings.CreateDefault() with { Profiles = [profile] });
        var before = WindowsDevices(SoundCore, Brio, SoundCore);
        var after = WindowsDevices(SoundCore, Brio, SoundCore, UnknownSpeaker);

        var prompt = Assert.IsType<PromptDecision>(Assert.Single(
            new RoutingEngine(rules).Decide(after.DiffFrom(before), after, WindowsRoutedTo(SoundCore, Brio, before), [])));

        AssertWindowsSwitch(prompt.Plan, UnknownSpeaker, null); // empty default mic order: the mic stays
        // With Sonar, the same profile keeps Windows on Sonar's Gaming and Chat devices.
        Assert.Equal(new WindowsDefaults(SonarGaming, SonarChat), rules.PickWindowsDefaults(UnknownSpeaker, after).Defaults);
    }

    [Fact]
    public void Windows_only_no_popup_when_the_windows_defaults_already_go_there()
    {
        var before = WindowsDevices(SoundCore, ArctisMic, ArctisMic);
        var after = WindowsDevices(SoundCore, ArctisMic, ArctisMic, SoundCore); // e.g. Windows picked it by itself

        Assert.IsType<NoActionDecision>(Assert.Single(
            Engine.Decide(after.DiffFrom(before), after, WindowsRoutedTo(SoundCore, ArctisMic, before), [])));
    }

    [Fact]
    public void Windows_only_device_in_use_going_away_falls_back_with_the_recording_device()
    {
        // SoundCore was the default; it turned off and Windows moved its default to the monitor by itself.
        var before = WindowsDevices(SoundCore, Brio, Arctis, ArctisMic, SoundCore);
        var after = WindowsDevices(MonitorHdmi, Brio, Arctis, ArctisMic);

        var auto = Assert.IsType<AutoSwitchDecision>(Assert.Single(
            Engine.Decide(after.DiffFrom(before), after, WindowsRoutedTo(MonitorHdmi, Brio, before), ["SteelSeries Arctis"])));

        AssertWindowsSwitch(auto.Plan, Arctis, ArctisMic);
        Assert.Equal("SoundCore 2 disconnected", auto.Cause);
    }

    [Fact]
    public void Windows_only_device_that_was_not_the_default_going_away_changes_nothing()
    {
        var before = WindowsDevices(Arctis, ArctisMic, Arctis, ArctisMic, SoundCore);
        var after = WindowsDevices(Arctis, ArctisMic, Arctis, ArctisMic);

        Assert.Empty(Engine.Decide(after.DiffFrom(before), after, WindowsRoutedTo(Arctis, ArctisMic, before), []));
    }

    [Fact]
    public void Windows_only_lost_recording_device_is_replaced_by_the_preference_of_the_device_playing()
    {
        var before = WindowsDevices(SoundCore, BrioAsBluetoothMic, SoundCore, BrioAsBluetoothMic);
        var after = WindowsDevices(SoundCore, Brio, SoundCore); // Windows moved the recording default to the Brio

        var auto = Assert.IsType<AutoSwitchDecision>(Assert.Single(
            Engine.Decide(after.DiffFrom(before), after, WindowsRoutedTo(SoundCore, Brio, before), [])));

        Assert.True(auto.Plan.WindowsOnly);
        Assert.Null(auto.Plan.Output);
        Assert.Equal(Brio, auto.Plan.Mic); // SoundCore 2: [Arctis mic, Brio], and no Arctis here
    }

    [Fact]
    public void Windows_only_startup_leaves_the_defaults_alone_and_still_disconnects_a_declined_device()
    {
        // The default is an ignored device (the monitor): the user's choice, not something to repair.
        var devices = WindowsDevices(MonitorHdmi, Brio, SoundCore, WhCh520);

        var disconnect = Assert.IsType<DisconnectDecision>(Assert.Single(
            Engine.DecideAtStartup(devices, WindowsRoutedTo(MonitorHdmi, Brio), [])));

        Assert.Equal(WhCh520, disconnect.Device);
    }

    [Fact]
    public void Undetermined_mode_neither_asks_nor_falls_back()
    {
        // Sonar not reachable and not yet known to be absent: the Windows default may be Sonar's and Sonar restarting.
        var current = CurrentRouting.Unknown with { WindowsDefaultOutput = SoundCore.Id };

        var decisions = Decide(Devices(true, SoundCore), Devices(true, UnknownSpeaker), current);

        Assert.All(decisions, d => Assert.IsType<NoActionDecision>(d));
        Assert.Contains("not asking", Assert.Single(decisions).Reason);
    }

    // --- Windows default display names

    [Fact]
    public void Windows_default_names_drop_the_device_name_only_when_the_label_is_unique()
    {
        var snapshot = Devices(true, SoundCore);
        var rules = new RoutingRules(Settings);

        // Sonar's labels are unique; "Headphones" is shared by the Arctis and the SoundCore.
        Assert.Equal("SteelSeries Sonar - Gaming, calls: SteelSeries Sonar - Chat", rules.PlanSwitchTo(Arctis, snapshot, "test").WindowsDefaults!.ToString());
        Assert.Equal("Headphones (SoundCore 2 Stereo)", rules.PlanSwitchTo(SoundCore, snapshot, "test").WindowsDefaults!.ToString());
    }

    // --- Built-in defaults (no profiles, no mic preference)

    static readonly AudioSwitchSettings Defaults = AudioSwitchSettings.CreateDefault();
    static readonly RoutingEngine DefaultEngine = new(new RoutingRules(Defaults));

    static AvailabilitySnapshot DefaultDevices(params AudioEndpoint[] outputs) =>
        AvailabilitySnapshot.Build(new[] { Brio, SoundCoreHandsFree }.Concat(outputs), null, new DeviceFilter(Defaults.ToFilterOptions()));

    [Fact]
    public void With_defaults_a_connecting_device_is_offered_as_its_own_windows_default_and_the_mic_is_left_alone()
    {
        var before = DefaultDevices(SoundCore);
        var after = DefaultDevices(SoundCore, UnknownSpeaker);

        var prompt = Assert.IsType<PromptDecision>(Assert.Single(
            DefaultEngine.Decide(after.DiffFrom(before), after, RoutedTo(SoundCore, Brio), [])));

        Assert.Equal("JBL Flip Stereo", prompt.DisplayName);
        Assert.Equal(UnknownSpeaker, prompt.Plan.Output);
        Assert.Equal(new WindowsDefaults(UnknownSpeaker, UnknownSpeaker), prompt.Plan.WindowsDefaults);
        Assert.Equal(SonarChannels.ClassicOutputs, prompt.Plan.Channels);
        Assert.Null(prompt.Plan.Mic);
        Assert.Contains("no mic preference for JBL Flip Stereo, mic unchanged", prompt.Plan.Reason);
        Assert.False(prompt.DisconnectWhenDeclined);
    }

    [Fact]
    public void With_defaults_the_fallback_is_the_most_recently_used_device()
    {
        var before = DefaultDevices(SoundCore, UnknownSpeaker, WhCh520);
        var after = DefaultDevices(UnknownSpeaker, WhCh520);

        var auto = Assert.IsType<AutoSwitchDecision>(Assert.Single(DefaultEngine.Decide(
            after.DiffFrom(before), after, RoutedTo(SoundCore, Brio), ["SoundCore 2 Stereo", "WH-CH520 Stereo", "JBL Flip Stereo"])));

        Assert.Equal(WhCh520, auto.Plan.Output);
        Assert.Null(auto.Plan.Mic);
        Assert.Contains("most recently used", auto.Reason);
    }

    [Fact]
    public void With_defaults_and_nothing_used_yet_the_fallback_is_the_first_output_by_name()
    {
        var before = DefaultDevices(SoundCore, UnknownSpeaker, WhCh520);
        var after = DefaultDevices(UnknownSpeaker, WhCh520);

        var auto = Assert.IsType<AutoSwitchDecision>(Assert.Single(
            DefaultEngine.Decide(after.DiffFrom(before), after, RoutedTo(SoundCore, Brio), [])));

        Assert.Equal(UnknownSpeaker, auto.Plan.Output); // "Headphones (JBL Flip Stereo)" sorts before "Headphones (WH-CH520 Stereo)"
        Assert.Contains("first by name", auto.Reason);
    }

    [Fact]
    public void With_defaults_startup_never_disconnects_anything() =>
        Assert.Empty(DefaultEngine.DecideAtStartup(DefaultDevices(SoundCore, WhCh520), RoutedTo(SoundCore, Brio), []));
}
