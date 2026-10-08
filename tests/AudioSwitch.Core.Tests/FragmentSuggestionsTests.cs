using AudioSwitch.Core.Audio;
using AudioSwitch.Core.Settings;
using static AudioSwitch.Core.Tests.TestEndpoints;

namespace AudioSwitch.Core.Tests;

public class FragmentSuggestionsTests
{
    [Fact]
    public void Device_names_drop_the_usb_instance_prefix()
    {
        var names = FragmentSuggestions.DeviceNames(All, EndpointFlow.Render);

        Assert.Contains("Arctis Nova Pro Wireless", names);
        Assert.DoesNotContain("8- Arctis Nova Pro Wireless", names);
        Assert.Single(names, n => n == "SteelSeries Sonar Virtual Audio Device"); // Gaming and Chat share it
    }

    [Fact]
    public void Device_names_only_list_the_requested_flow()
    {
        var mics = FragmentSuggestions.DeviceNames(All, EndpointFlow.Capture);

        Assert.Contains("Brio 105", mics);
        Assert.DoesNotContain("SoundCore 2 Stereo", mics);
    }

    [Fact]
    public void Endpoint_labels_use_the_unique_endpoint_name_and_fall_back_to_the_device_name()
    {
        var labels = FragmentSuggestions.EndpointLabels([.. All, SonarChat, WhCh520], EndpointFlow.Render);

        Assert.Contains("SteelSeries Sonar - Gaming", labels);
        Assert.Contains("SteelSeries Sonar - Chat", labels);
        Assert.Contains("32G2WG8", labels);
        // "Headphones" is shared by the Arctis, SoundCore and WH-CH520, so it would be ambiguous.
        Assert.DoesNotContain("Headphones", labels);
        Assert.Contains("Arctis Nova Pro Wireless", labels);
        Assert.Contains("WH-CH520 Stereo", labels);
    }

    [Fact]
    public void Every_output_has_a_suggestion_that_matches_it()
    {
        foreach (var e in All.Where(e => e.Flow == EndpointFlow.Render))
            Assert.Contains(FragmentSuggestions.EndpointLabels(All, EndpointFlow.Render), s => e.Name.Contains(s, StringComparison.OrdinalIgnoreCase));
    }
}
