using AudioSwitch.Core.Routing;
using AudioSwitch.Core.Sonar;

namespace AudioSwitch.Core.Tests;

/// <summary>When AudioSwitch manages Sonar, only Windows, or nothing yet (README "Without SteelSeries").</summary>
public class SwitchingModeRuleTests
{
    static readonly TimeSpan Grace = SwitchingModeRule.DefaultGrace;
    static readonly DateTimeOffset T0 = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Sonar_answering_means_sonar_mode() =>
        Assert.Equal(SwitchingMode.Sonar, new SwitchingModeRule(Grace).Next(SonarPresence.Running, T0).Mode);

    [Fact]
    public void Gg_not_installed_means_windows_only_right_away()
    {
        var verdict = new SwitchingModeRule(Grace).Next(SonarPresence.NotInstalled, T0);

        Assert.Equal(SwitchingMode.WindowsOnly, verdict.Mode);
        Assert.Equal("GG not installed", verdict.Reason);
    }

    [Fact]
    public void Sonar_turned_off_in_gg_means_windows_only_right_away() =>
        Assert.Equal(SwitchingMode.WindowsOnly, new SwitchingModeRule(Grace).Next(SonarPresence.Disabled, T0).Mode);

    [Fact]
    public void Gg_installed_but_not_answering_waits_for_the_grace_period_before_windows_only()
    {
        var rule = new SwitchingModeRule(Grace);

        Assert.Equal(SwitchingMode.Undetermined, rule.Next(SonarPresence.NotReachable, T0).Mode);
        Assert.Equal(SwitchingMode.Undetermined, rule.Next(SonarPresence.NotReachable, T0 + Grace - TimeSpan.FromSeconds(1)).Mode);
        var verdict = rule.Next(SonarPresence.NotReachable, T0 + Grace);
        Assert.Equal(SwitchingMode.WindowsOnly, verdict.Mode);
        Assert.Equal("GG not running", verdict.Reason);
    }

    [Fact]
    public void Sonar_starting_never_leads_to_windows_only()
    {
        var rule = new SwitchingModeRule(Grace);

        rule.Next(SonarPresence.Starting, T0);
        Assert.Equal(SwitchingMode.Undetermined, rule.Next(SonarPresence.Starting, T0 + TimeSpan.FromMinutes(30)).Mode);
    }

    [Fact]
    public void The_grace_period_restarts_when_gg_answers_in_between()
    {
        var rule = new SwitchingModeRule(Grace);
        rule.Next(SonarPresence.NotReachable, T0);
        rule.Next(SonarPresence.Starting, T0 + TimeSpan.FromSeconds(100)); // GG is up, Sonar starting
        var again = T0 + TimeSpan.FromSeconds(110);
        rule.Next(SonarPresence.NotReachable, again);

        Assert.Equal(SwitchingMode.Undetermined, rule.Next(SonarPresence.NotReachable, T0 + Grace + TimeSpan.FromSeconds(1)).Mode);
        Assert.Equal(SwitchingMode.WindowsOnly, rule.Next(SonarPresence.NotReachable, again + Grace).Mode);
    }

    [Fact]
    public void Sonar_lost_while_in_use_is_undetermined_not_windows_only()
    {
        var rule = new SwitchingModeRule(Grace);
        rule.Next(SonarPresence.Running, T0);

        Assert.Equal(SwitchingMode.Undetermined, rule.Next(SonarPresence.NotReachable, T0 + TimeSpan.FromSeconds(5)).Mode);
    }

    [Fact]
    public void Sonar_returning_ends_windows_only_mode()
    {
        var rule = new SwitchingModeRule(Grace);
        rule.Next(SonarPresence.NotInstalled, T0);

        Assert.Equal(SwitchingMode.Sonar, rule.Next(SonarPresence.Running, T0 + TimeSpan.FromSeconds(5)).Mode);
    }

    [Fact]
    public void Windows_only_mode_stays_when_gg_stops_answering()
    {
        var rule = new SwitchingModeRule(Grace);
        rule.Next(SonarPresence.Disabled, T0);

        var verdict = rule.Next(SonarPresence.NotReachable, T0 + TimeSpan.FromSeconds(5)); // GG closed
        Assert.Equal(SwitchingMode.WindowsOnly, verdict.Mode);
        Assert.Equal("GG not running", verdict.Reason);
    }
}
