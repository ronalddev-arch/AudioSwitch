using AudioSwitch.Core.Audio;

namespace AudioSwitch.Core.Tests;

/// <summary>Realistic example endpoints: names, buses and id shapes as Windows reports them; the ids are made up.</summary>
static class TestEndpoints
{
    public static readonly AudioEndpoint Arctis = new("{0.0.0.00000000}.{a1000000-0000-4000-8000-000000000001}",
        "Headphones (8- Arctis Nova Pro Wireless)", "8- Arctis Nova Pro Wireless", EndpointFlow.Render, "USB");
    public static readonly AudioEndpoint ArctisMic = new("{0.0.1.00000000}.{a1000000-0000-4000-8000-000000000002}",
        "Microphone (8- Arctis Nova Pro Wireless)", "8- Arctis Nova Pro Wireless", EndpointFlow.Capture, "USB");
    public static readonly AudioEndpoint SoundCore = new("{0.0.0.00000000}.{b2000000-0000-4000-8000-000000000001}",
        "Headphones (SoundCore 2 Stereo)", "SoundCore 2 Stereo", EndpointFlow.Render, "BTHENUM");
    public static readonly AudioEndpoint SoundCoreHandsFree = new("{0.0.0.00000000}.{b2000000-0000-4000-8000-000000000002}",
        "Headset (SoundCore 2 Hands-Free AG Audio)", "SoundCore 2 Hands-Free AG Audio", EndpointFlow.Render, "BTHHFENUM");
    public static readonly AudioEndpoint SoundCoreHandsFreeMic = new("{0.0.1.00000000}.{b2000000-0000-4000-8000-000000000003}",
        "Headset (SoundCore 2 Hands-Free AG Audio)", "SoundCore 2 Hands-Free AG Audio", EndpointFlow.Capture, "BTHHFENUM");
    public static readonly AudioEndpoint MonitorHdmi = new("{0.0.0.00000000}.{c3000000-0000-4000-8000-000000000001}",
        "32G2WG8 (NVIDIA High Definition Audio)", "NVIDIA High Definition Audio", EndpointFlow.Render, "HDAUDIO");
    public static readonly AudioEndpoint SonarGaming = new("{0.0.0.00000000}.{d4000000-0000-4000-8000-000000000001}",
        "SteelSeries Sonar - Gaming (SteelSeries Sonar Virtual Audio Device)", "SteelSeries Sonar Virtual Audio Device", EndpointFlow.Render, "ROOT");
    public static readonly AudioEndpoint SonarChat = new("{0.0.0.00000000}.{d4000000-0000-4000-8000-000000000002}",
        "SteelSeries Sonar - Chat (SteelSeries Sonar Virtual Audio Device)", "SteelSeries Sonar Virtual Audio Device", EndpointFlow.Render, "ROOT");
    public static readonly AudioEndpoint Brio =new("{0.0.1.00000000}.{e5000000-0000-4000-8000-000000000001}",
        "Microphone (Brio 105)", "Brio 105", EndpointFlow.Capture, "USB");

    public static readonly AudioEndpoint WhCh520 = new("{0.0.0.00000000}.{f6000000-0000-4000-8000-000000000001}",
        "Headphones (WH-CH520 Stereo)", "WH-CH520 Stereo", EndpointFlow.Render, "BTHENUM");
    public static readonly AudioEndpoint UnknownSpeaker = new("{0.0.0.00000000}.{07000000-0000-4000-8000-000000000001}",
        "Headphones (JBL Flip Stereo)", "JBL Flip Stereo", EndpointFlow.Render, "BTHENUM");

    public static readonly AudioEndpoint[] All = [Arctis, ArctisMic, SoundCore, SoundCoreHandsFree, SoundCoreHandsFreeMic, MonitorHdmi, SonarGaming, Brio];
}
