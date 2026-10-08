using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace AudioSwitch.Core.Audio;

public sealed class WindowsAudio : IWindowsAudio
{
    // PKEY_Device_EnumeratorName: "USB", "BTHENUM" (A2DP), "BTHHFENUM" (hands-free), "HDAUDIO", "ROOT" (virtual).
    static readonly PropertyKey EnumeratorNameKey = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 24);
    // PKEY_Device_ContainerId: one GUID per physical device.
    static readonly PropertyKey ContainerIdKey = new(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);

    readonly MMDeviceEnumerator _notificationEnumerator = new();
    readonly MMDeviceNotificationClient _notifications;

    public WindowsAudio()
    {
        _notifications = _notificationEnumerator.CreateNotificationClient(useSynchronizationContext: false);
        _notifications.DeviceAdded += (_, e) => Raise($"endpoint added {e.DeviceId}");
        _notifications.DeviceRemoved += (_, e) => Raise($"endpoint removed {e.DeviceId}");
        _notifications.DeviceStateChanged += (_, e) => Raise($"endpoint {e.DeviceId} -> {e.NewState}");
        _notifications.DefaultDeviceChanged += (_, e) => Raise($"default {e.Flow}/{e.Role} -> {e.DeviceId ?? "none"}");
    }

    public event EventHandler<string>? Changed;

    void Raise(string description) => Changed?.Invoke(this, description);

    public IReadOnlyList<AudioEndpoint> GetActiveEndpoints()
    {
        // A fresh enumerator per query keeps us independent of the calling thread's COM apartment.
        using var enumerator = new MMDeviceEnumerator();
        var result = new List<AudioEndpoint>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active))
        {
            using (device)
            {
                try
                {
                    var flow = device.DataFlow == DataFlow.Render ? EndpointFlow.Render : EndpointFlow.Capture;
                    var properties = device.Properties;
                    var bus = properties.Contains(EnumeratorNameKey) ? properties[EnumeratorNameKey].Value?.ToString() ?? "" : "";
                    var container = properties.Contains(ContainerIdKey) && properties[ContainerIdKey].Value is Guid g ? g : (Guid?)null;
                    result.Add(new AudioEndpoint(device.ID, device.FriendlyName, device.DeviceFriendlyName, flow, bus, container));
                }
                catch (COMException)
                {
                    // The endpoint disappeared while we were enumerating; the next change notification re-evaluates.
                }
            }
        }
        return result;
    }

    public string? GetDefaultOutputId()
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Console)) return null;
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
        return device.ID;
    }

    public string? GetDefaultInputId()
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Console)) return null;
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        return device.ID;
    }

    public void SetDefaultInput(string endpointId)
    {
        // IPolicyConfig takes any endpoint id; the Sound control panel uses it for recording devices too.
        PolicyConfig.SetDefaultEndpoint(endpointId, Role.Console);
        PolicyConfig.SetDefaultEndpoint(endpointId, Role.Multimedia);
        PolicyConfig.SetDefaultEndpoint(endpointId, Role.Communications);
    }

    public void SetDefaultOutput(string endpointId, OutputRoles roles)
    {
        if (roles.HasFlag(OutputRoles.Playback))
        {
            PolicyConfig.SetDefaultEndpoint(endpointId, Role.Console);
            PolicyConfig.SetDefaultEndpoint(endpointId, Role.Multimedia);
        }
        if (roles.HasFlag(OutputRoles.Communications))
            PolicyConfig.SetDefaultEndpoint(endpointId, Role.Communications);
    }

    public IReadOnlyList<AudioEndpoint> DisconnectBluetooth(AudioEndpoint endpoint)
    {
        // Disconnect every Bluetooth endpoint of the same physical device (stereo + hands-free in/out), otherwise the
        // hands-free profile keeps the device connected.
        var siblings = GetActiveEndpoints()
            .Where(e => e.Id == endpoint.Id || (endpoint.ContainerId is { } c && e.ContainerId == c))
            .Where(e => e.Bus.StartsWith("BTH", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (siblings.Count == 0)
            throw new InvalidOperationException($"{endpoint.Name} is not an active Bluetooth endpoint.");

        var disconnected = new List<AudioEndpoint>();
        var errors = new List<Exception>();
        foreach (var sibling in siblings)
        {
            try
            {
                BluetoothAudio.Disconnect(sibling.Id);
                disconnected.Add(sibling);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                errors.Add(ex);
            }
        }
        if (disconnected.Count == 0)
            throw new AggregateException($"Could not disconnect {endpoint.Name}.", errors);
        return disconnected;
    }

    public void Dispose()
    {
        _notifications.Dispose();
        _notificationEnumerator.Dispose();
    }
}
