using AudioSwitch.Core.Sonar;

namespace AudioSwitch.Core.Tests;

public class SteelSeriesVersionsTests
{
    [Fact]
    public void Build_embeds_the_verified_versions_from_directory_build_props()
    {
        var verified = SteelSeriesVersions.Verified(typeof(SteelSeriesVersions).Assembly);

        Assert.False(string.IsNullOrEmpty(verified.Gg));
        Assert.False(string.IsNullOrEmpty(verified.Sonar));
    }

    [Theory]
    [InlineData("120.0.0", "1.103.0.0", false)]
    [InlineData("121.0.0", "1.103.0.0", true)]
    [InlineData("120.0.0", "1.104.0.0", true)]
    [InlineData(null, null, false)] // detection failed: don't nag
    public void Detects_gg_or_sonar_updates(string? gg, string? sonar, bool differs) =>
        Assert.Equal(differs, new SteelSeriesVersions(gg, sonar).DiffersFrom(new SteelSeriesVersions("120.0.0", "1.103.0.0")));
}
