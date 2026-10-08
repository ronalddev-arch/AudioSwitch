using AudioSwitch.Core.Audio;
using static AudioSwitch.Core.Tests.TestEndpoints;

namespace AudioSwitch.Core.Tests;

public class MonitorAudioTests
{
    static AudioEndpoint Output(string name, string deviceName, string bus = "HDAUDIO") =>
        new($"{{0.0.0.00000000}}.{{{Guid.NewGuid()}}}", name, deviceName, EndpointFlow.Render, bus);

    // Endpoint names as Windows shows them (several from a real MMDevices registry, the rest from the
    // graphics vendors' drivers).
    [Theory]
    [InlineData("32G2WG8 (NVIDIA High Definition Audio)", "NVIDIA High Definition Audio")]
    [InlineData("NVIDIA Output (NVIDIA High Definition Audio)", "NVIDIA High Definition Audio")]
    [InlineData("Digital Audio (HDMI) (High Definition Audio Device)", "High Definition Audio Device")]
    [InlineData("DELL S2721DGF (AMD High Definition Audio Device)", "AMD High Definition Audio Device")]
    [InlineData("LG ULTRAGEAR (Intel(R) Display Audio)", "Intel(R) Display Audio")]
    [InlineData("Digital Audio (DisplayPort) (High Definition Audio Device)", "High Definition Audio Device")]
    [InlineData("Realtek HDMI Output (Realtek High Definition Audio)", "Realtek High Definition Audio")]
    [InlineData("SAMSUNG (ATI HDMI Audio)", "ATI HDMI Audio")]
    public void Monitor_and_tv_outputs_are_recognised(string name, string deviceName) =>
        Assert.True(MonitorAudio.IsMonitorAudio(Output(name, deviceName)));

    [Theory]
    [InlineData("Speakers (Realtek(R) Audio)", "Realtek(R) Audio", "HDAUDIO")]
    [InlineData("Headphones (Realtek(R) Audio)", "Realtek(R) Audio", "HDAUDIO")]
    [InlineData("Realtek Digital Output (Realtek(R) Audio)", "Realtek(R) Audio", "HDAUDIO")] // S/PDIF, not a monitor
    [InlineData("Speakers (High Definition Audio Device)", "High Definition Audio Device", "HDAUDIO")] // generic onboard driver
    [InlineData("Speakers (Intel(R) Smart Sound Technology for USB Audio)", "Intel(R) Smart Sound Technology for USB Audio", "HDAUDIO")]
    [InlineData("Speakers (USB Audio)", "USB Audio", "USB")]
    [InlineData("Speakers (NVIDIA Broadcast)", "NVIDIA Broadcast", "ROOT")]
    [InlineData("Headphones (8- Arctis Nova Pro Wireless)", "8- Arctis Nova Pro Wireless", "USB")]
    [InlineData("Headphones (SoundCore 2 Stereo)", "SoundCore 2 Stereo", "BTHENUM")]
    public void Built_in_and_other_outputs_are_not_monitors(string name, string deviceName, string bus) =>
        Assert.False(MonitorAudio.IsMonitorAudio(Output(name, deviceName, bus)));

    [Fact]
    public void An_hdmi_capture_device_is_not_a_monitor() =>
        Assert.False(MonitorAudio.IsMonitorAudio(new AudioEndpoint("{0.0.1.00000000}.{c1}", "HDMI (Cam Link 4K)", "Cam Link 4K", EndpointFlow.Capture, "USB")));

    [Fact]
    public void Graphics_card_audio_is_ignored_by_device_name_so_every_monitor_on_it_is_covered() =>
        Assert.Equal("NVIDIA High Definition Audio", MonitorAudio.ExclusionFragment(MonitorHdmi));

    [Theory]
    [InlineData("Digital Audio (HDMI) (High Definition Audio Device)", "High Definition Audio Device", "(HDMI)")]
    [InlineData("Digital Audio (DisplayPort) (High Definition Audio Device)", "High Definition Audio Device", "DisplayPort")]
    [InlineData("LG ULTRAGEAR (Intel(R) Display Audio)", "Intel(R) Display Audio", "Intel(R) Display Audio")]
    public void Other_monitor_audio_is_ignored_by_its_hdmi_or_displayport_part(string name, string deviceName, string fragment)
    {
        var monitor = Output(name, deviceName);

        Assert.Equal(fragment, MonitorAudio.ExclusionFragment(monitor));
        // Never the generic driver's name, which the PC's own speakers can share.
        Assert.DoesNotContain(MonitorAudio.ExclusionFragment(monitor), "Speakers (High Definition Audio Device)", StringComparison.OrdinalIgnoreCase);
    }
}
