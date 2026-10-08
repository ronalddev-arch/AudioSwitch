using AudioSwitch.Core.Sonar;

namespace AudioSwitch.Core.Tests;

/// <summary>Translating a plan's output channels when Sonar switched modes between planning and applying.</summary>
public class SonarChannelsTests
{
    [Fact]
    public void Classic_channels_become_the_personal_mix_in_stream_mode() =>
        Assert.Equal([SonarChannel.Monitoring], SonarChannels.ForMode([SonarChannel.Game, SonarChannel.Chat], SonarMode.Stream));

    [Fact]
    public void The_personal_mix_becomes_every_classic_channel_in_classic_mode() =>
        Assert.Equal(SonarChannels.ClassicOutputs, SonarChannels.ForMode(SonarChannels.StreamOutputs, SonarMode.Classic));

    [Fact]
    public void Channels_already_in_the_right_mode_stay_as_they_are() =>
        Assert.Equal([SonarChannel.Game, SonarChannel.Media], SonarChannels.ForMode([SonarChannel.Game, SonarChannel.Media], SonarMode.Classic));

    [Theory]
    [InlineData(SonarMode.Classic)]
    [InlineData(SonarMode.Stream)]
    public void No_output_channels_stay_none(SonarMode mode) => Assert.Empty(SonarChannels.ForMode([], mode));

    [Fact]
    public void The_popup_shows_one_mix_for_a_stream_plan_and_four_channels_otherwise()
    {
        Assert.Equal(SonarChannels.StreamOutputs, SonarChannels.OutputSetOf(SonarChannels.StreamOutputs));
        Assert.Equal(SonarChannels.ClassicOutputs, SonarChannels.OutputSetOf([SonarChannel.Chat]));
    }
}
