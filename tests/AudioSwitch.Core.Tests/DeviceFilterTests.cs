using AudioSwitch.Core.Watching;
using static AudioSwitch.Core.Tests.TestEndpoints;

namespace AudioSwitch.Core.Tests;

public class DeviceFilterTests
{
    readonly DeviceFilter _filter = new(ExampleSettings.Sample.ToFilterOptions());

    [Fact]
    public void Allows_real_outputs_and_mics()
    {
        Assert.Null(_filter.GetExclusionReason(Arctis));
        Assert.Null(_filter.GetExclusionReason(ArctisMic));
        Assert.Null(_filter.GetExclusionReason(SoundCore));
        Assert.Null(_filter.GetExclusionReason(Brio));
    }

    [Fact]
    public void Excluded_name_fragments_ignore_monitor_audio() =>
        Assert.Contains("NVIDIA High Definition Audio", _filter.GetExclusionReason(MonitorHdmi));

    [Fact]
    public void Default_exclusions_name_no_specific_hardware()
    {
        var defaults = new DeviceFilter(new DeviceFilterOptions());

        Assert.Null(defaults.GetExclusionReason(MonitorHdmi)); // "NVIDIA High Definition Audio" is an exclusion of the sample settings only
        Assert.NotNull(defaults.GetExclusionReason(SoundCoreHandsFree));
        Assert.NotNull(defaults.GetExclusionReason(SonarGaming));
    }

    [Fact]
    public void Excludes_bluetooth_hands_free_endpoints()
    {
        Assert.NotNull(_filter.GetExclusionReason(SoundCoreHandsFree));
        Assert.NotNull(_filter.GetExclusionReason(SoundCoreHandsFreeMic));
    }

    [Fact]
    public void Excludes_generic_hdmi_audio() =>
        Assert.NotNull(new DeviceFilter(new DeviceFilterOptions()).GetExclusionReason(MonitorHdmi with { Name = "Digital Audio (HDMI) (High Definition Audio Device)", DeviceName = "High Definition Audio Device" }));

    [Fact]
    public void Excludes_sonar_virtual_devices() =>
        Assert.Equal("Sonar virtual device", _filter.GetExclusionReason(SonarGaming));
}
