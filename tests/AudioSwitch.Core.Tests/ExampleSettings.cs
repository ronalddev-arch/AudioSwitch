using AudioSwitch.Core.Settings;

namespace AudioSwitch.Core.Tests;

/// <summary>Settings the tests run against.</summary>
static class ExampleSettings
{
    /// <summary>
    /// A sample configuration, the built-in defaults up to v0.4.1: a SteelSeries headset routed through
    /// Sonar's Gaming/Chat devices, a Bluetooth speaker, and a shared Bluetooth headset that is disconnected when
    /// declined; the webcam mic (Brio) when the headset is off; the GPU's monitor audio ignored.
    /// </summary>
    public static readonly AudioSwitchSettings Sample = AudioSwitchSettings.CreateDefault() with
    {
        ExcludedNameFragments = ["(HDMI)", "NVIDIA High Definition Audio", "Hands-Free"],
        Profiles =
        [
            new()
            {
                Name = "SteelSeries Arctis",
                Match = "Arctis Nova Pro Wireless",
                MicPreference = ["Arctis Nova Pro Wireless"],
                WindowsDefault = "SteelSeries Sonar - Gaming",
                WindowsCommunicationsDefault = "SteelSeries Sonar - Chat",
            },
            new() { Name = "SoundCore 2", Match = "SoundCore 2", MicPreference = ["Arctis Nova Pro Wireless", "Brio 105"] },
            new() { Name = "WH-CH520 (shared)", Match = "WH-CH520", MicPreference = ["Brio 105"], DisconnectWhenDeclined = true },
        ],
        DefaultMicPreference = ["Arctis Nova Pro Wireless", "Brio 105"],
    };
}
