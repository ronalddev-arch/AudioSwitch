namespace AudioSwitch.Core.Audio;

public enum EndpointFlow { Render, Capture }

/// <summary>
/// An active Windows audio endpoint. <see cref="Id"/> is the MMDevice endpoint id, which Sonar uses verbatim.
/// </summary>
/// <param name="Name">Endpoint name as shown by Windows, e.g. "Headphones (SoundCore 2 Stereo)".</param>
/// <param name="DeviceName">Adapter/device name, e.g. "SoundCore 2 Stereo".</param>
/// <param name="Bus">PnP enumerator of the device, e.g. "USB", "BTHENUM", "BTHHFENUM", "HDAUDIO", "ROOT".</param>
/// <param name="ContainerId">Shared by all endpoints of one physical device (e.g. a Bluetooth headset's stereo and
/// hands-free endpoints).</param>
public sealed record AudioEndpoint(string Id, string Name, string DeviceName, EndpointFlow Flow, string Bus, Guid? ContainerId = null)
{
    /// <summary><see cref="DeviceName"/> without the instance prefix Windows adds to re-enumerated USB devices
    /// ("8- Arctis Nova Pro Wireless" → "Arctis Nova Pro Wireless"), for display.</summary>
    public string DisplayDeviceName => System.Text.RegularExpressions.Regex.Replace(DeviceName, @"^\d+- ", "");

    public override string ToString() => Name;
}
