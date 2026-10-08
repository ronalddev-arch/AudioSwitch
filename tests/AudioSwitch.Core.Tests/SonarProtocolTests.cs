using System.Text.Json.Nodes;
using AudioSwitch.Core.Sonar;

namespace AudioSwitch.Core.Tests;

/// <summary>Samples are trimmed copies of real responses from GG 120.0.0 / Sonar 1.103; the files in Fixtures/ are
/// untrimmed. Device ids in both are made up.</summary>
public class SonarProtocolTests
{
    const string SoundCoreId = "{0.0.0.00000000}.{b2000000-0000-4000-8000-000000000001}";
    const string EscapedSoundCoreId = "%7B0.0.0.00000000%7D.%7Bb2000000-0000-4000-8000-000000000001%7D";

    static JsonNode Fixture(string name) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)))!;

    [Fact]
    public void Set_route_escapes_device_id_like_gg_does() =>
        Assert.Equal(
            $"classicRedirections/game/deviceId/{EscapedSoundCoreId}",
            SonarProtocol.SetRedirectionRoute(SonarMode.Classic, SonarChannel.Game, SoundCoreId));

    [Theory]
    [InlineData(SonarMode.Stream, SonarChannel.Monitoring, "streamRedirections/monitoring/deviceId/")]
    [InlineData(SonarMode.Stream, SonarChannel.Mic, "streamRedirections/mic/deviceId/")]
    [InlineData(SonarMode.Classic, SonarChannel.Mic, "classicRedirections/mic/deviceId/")]
    [InlineData(SonarMode.Unknown, SonarChannel.Chat, "classicRedirections/chat/deviceId/")]
    public void Set_route_uses_the_redirections_of_the_mode(SonarMode mode, SonarChannel channel, string prefix) =>
        Assert.Equal(prefix + EscapedSoundCoreId, SonarProtocol.SetRedirectionRoute(mode, channel, SoundCoreId));

    [Theory]
    [InlineData(SonarMode.Stream, SonarChannel.Game)]
    [InlineData(SonarMode.Classic, SonarChannel.Monitoring)]
    public void Set_route_rejects_a_channel_of_the_other_mode(SonarMode mode, SonarChannel channel) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SonarProtocol.SetRedirectionRoute(mode, channel, SoundCoreId));

    [Theory]
    [InlineData("classic", SonarMode.Classic)]
    [InlineData("stream", SonarMode.Stream)]
    [InlineData("streamer", SonarMode.Stream)]
    [InlineData("holographic", SonarMode.Unknown)]
    [InlineData(null, SonarMode.Unknown)]
    public void Parses_mode(string? mode, SonarMode expected) => Assert.Equal(expected, SonarProtocol.ParseMode(mode));

    [Fact]
    public void Parses_stream_redirections_personal_mix_and_mic_but_not_the_stream_mix()
    {
        var routing = SonarProtocol.ParseStreamRedirections(Fixture("streamRedirections.stream-mode.json"));

        Assert.Equal("{0.0.0.00000000}.{08000000-0000-4000-8000-000000000002}", routing[SonarChannel.Monitoring]);
        Assert.Equal("{0.0.1.00000000}.{08000000-0000-4000-8000-000000000003}", routing[SonarChannel.Mic]);
        Assert.Equal(2, routing.Count); // "streaming" (…000000000001) is the audience's mix: never read or written
    }

    [Fact]
    public void Stream_redirections_that_were_never_set_up_read_as_cleared()
    {
        // Captured live in classic mode, from a Sonar where stream mode had never been used.
        var routing = SonarProtocol.ParseStreamRedirections(Fixture("streamRedirections.classic-mode.json"));

        Assert.Equal("", routing[SonarChannel.Monitoring]);
        Assert.Equal("", routing[SonarChannel.Mic]);
    }

    [Fact]
    public void Parses_cleared_classic_redirections()
    {
        var routing = SonarProtocol.ParseRedirections(Fixture("classicRedirections.cleared.json"));

        Assert.All(SonarChannels.ClassicOutputs, c => Assert.Equal("", routing[c]));
        Assert.Equal("{0.0.1.00000000}.{e5000000-0000-4000-8000-000000000001}", routing[SonarChannel.Mic]);
    }

    [Theory]
    [InlineData(SonarChannel.Chat, "chat")]
    [InlineData(SonarChannel.Mic, "mic")]
    public void Redirection_keys_use_chat_and_mic(SonarChannel channel, string key) =>
        Assert.Equal(key, SonarProtocol.RedirectionKey(channel));

    [Fact]
    public void Parses_sonar_address_from_sub_apps()
    {
        var subApps = JsonNode.Parse("""
            {"subApps":{"sonar":{"name":"sonar","isEnabled":true,"isReady":true,"isRunning":true,
              "metadata":{"encryptedWebServerAddress":"","webServerAddress":"http://127.0.0.1:52983"}}}}
            """)!;

        Assert.Equal(new Uri("http://127.0.0.1:52983/"), SonarProtocol.ParseSonarAddress(subApps));
    }

    [Fact]
    public void Sonar_not_ready_is_reported_as_unavailable()
    {
        var subApps = JsonNode.Parse("""
            {"subApps":{"sonar":{"isEnabled":true,"isReady":false,"isRunning":true,"metadata":{"webServerAddress":""}}}}
            """)!;

        var ex = Assert.Throws<SonarUnavailableException>(() => SonarProtocol.ParseSonarAddress(subApps));
        Assert.Contains("isReady", ex.Message);
        Assert.Equal(SonarPresence.Starting, ex.Presence);
    }

    /// <summary>Tells "Sonar is turned off" (Windows-only mode right away) from "Sonar is starting" (wait).</summary>
    [Theory]
    [InlineData("""{"subApps":{"sonar":{"isEnabled":false,"isReady":false,"isRunning":false}}}""", SonarPresence.Disabled)]
    [InlineData("""{"subApps":{"engine":{"isEnabled":true}}}""", SonarPresence.Disabled)]
    [InlineData("""{"subApps":{"sonar":{"isEnabled":true,"isReady":false,"isRunning":false}}}""", SonarPresence.Starting)]
    public void Sub_apps_say_whether_sonar_is_turned_off_or_starting(string json, SonarPresence presence) =>
        Assert.Equal(presence, Assert.Throws<SonarUnavailableException>(() => SonarProtocol.ParseSonarAddress(JsonNode.Parse(json)!)).Presence);

    [Fact]
    public void Parses_classic_redirections()
    {
        var json = JsonNode.Parse("""
            [{"id":"aux","deviceId":"A","isRunning":true},{"id":"chat","deviceId":"A","isRunning":true},
             {"id":"game","deviceId":"B","isRunning":true},{"id":"media","deviceId":"A","isRunning":true},
             {"id":"mic","deviceId":"M","isRunning":true}]
            """)!;

        var routing = SonarProtocol.ParseRedirections(json);

        Assert.Equal("B", routing[SonarChannel.Game]);
        Assert.Equal("A", routing[SonarChannel.Chat]);
        Assert.Equal("M", routing[SonarChannel.Mic]);
        Assert.Equal(5, routing.Count);
    }

    [Fact]
    public void Parses_headset_state_from_fallback_lists_and_ignores_non_steelseries_devices()
    {
        var json = JsonNode.Parse("""
            {"game":[
               {"label":"Headphones (8- Arctis Nova Pro Wireless)","isActive":true,"isSteelseriesWirelessSupported":true,
                "isSteelseriesWirelessConnected":true,"id":"arctis-8","isExcluded":false},
               {"label":"Headphones (6- Arctis Nova Pro Wireless)","isActive":false,"isSteelseriesWirelessSupported":true,
                "isSteelseriesWirelessConnected":false,"id":"arctis-6","isExcluded":false},
               {"label":"Headphones (SoundCore 2 Stereo)","isActive":true,"isSteelseriesWirelessSupported":false,
                "isSteelseriesWirelessConnected":false,"id":"soundcore","isExcluded":false}],
             "chatCapture":[
               {"label":"Microphone (8- Arctis Nova Pro Wireless)","isActive":true,"isSteelseriesWirelessSupported":true,
                "isSteelseriesWirelessConnected":true,"id":"arctis-8-mic","isExcluded":false}]}
            """)!;

        var states = SonarProtocol.ParseWirelessStates(json);

        Assert.True(states["arctis-8"]);
        Assert.False(states["arctis-6"]);
        Assert.True(states["arctis-8-mic"]);
        Assert.False(states.ContainsKey("soundcore"));
    }
}
