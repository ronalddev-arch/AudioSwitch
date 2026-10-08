using System.Runtime.InteropServices;

namespace AudioSwitch.Core.Audio;

/// <summary>
/// Disconnects a Bluetooth audio endpoint the way the Settings app does: endpoint → IDeviceTopology → connector 0 →
/// connected part → KS filter device → IKsControl with KSPROPSETID_BtAudio / KSPROPERTY_ONESHOT_DISCONNECT.
/// Verified on Windows 10 19045 without admin rights.
/// </summary>
internal static class BluetoothAudio
{
    static readonly Guid KSPROPSETID_BtAudio = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    const uint KSPROPERTY_ONESHOT_DISCONNECT = 1, KSPROPERTY_TYPE_GET = 1;
    const int CLSCTX_ALL = 0x17;

    public static void Disconnect(string endpointId)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        Check(enumerator.GetDevice(endpointId, out var endpoint));
        var iidTopology = typeof(IDeviceTopology).GUID;
        Check(endpoint.Activate(ref iidTopology, CLSCTX_ALL, IntPtr.Zero, out var topologyObject));
        Check(((IDeviceTopology)topologyObject).GetConnector(0, out var connector));
        Check(connector.GetConnectedTo(out var filterConnector));
        Check(((IPart)filterConnector).GetTopologyObject(out var filterTopology));
        Check(filterTopology.GetDeviceId(out var filterDeviceId));
        Check(enumerator.GetDevice(filterDeviceId, out var filterDevice));
        var iidKsControl = typeof(IKsControl).GUID;
        Check(filterDevice.Activate(ref iidKsControl, CLSCTX_ALL, IntPtr.Zero, out var ksObject));
        var property = new KsProperty { Set = KSPROPSETID_BtAudio, Id = KSPROPERTY_ONESHOT_DISCONNECT, Flags = KSPROPERTY_TYPE_GET };
        Check(((IKsControl)ksObject).KsProperty(ref property, (uint)Marshal.SizeOf<KsProperty>(), IntPtr.Zero, 0, out _));
    }

    static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    [StructLayout(LayoutKind.Sequential)]
    struct KsProperty { public Guid Set; public uint Id; public uint Flags; }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorComObject;

    // Interfaces are declared only up to the methods used; earlier slots keep the vtable order.

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints();
        [PreserveSig] int GetDefaultAudioEndpoint();
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDeviceTopology
    {
        [PreserveSig] int GetConnectorCount(out uint count);
        [PreserveSig] int GetConnector(uint index, out IConnector connector);
        [PreserveSig] int GetSubunitCount(out uint count);
        [PreserveSig] int GetSubunit();
        [PreserveSig] int GetPartById();
        [PreserveSig] int GetDeviceId([MarshalAs(UnmanagedType.LPWStr)] out string deviceId);
    }

    [ComImport, Guid("9c2c4058-23f5-41de-877a-df3af236a09e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IConnector
    {
        [PreserveSig] int GetConnectorType(out int type);
        [PreserveSig] int GetDataFlow(out int flow);
        [PreserveSig] int ConnectTo(IConnector other);
        [PreserveSig] int Disconnect();
        [PreserveSig] int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);
        [PreserveSig] int GetConnectedTo(out IConnector other);
    }

    [ComImport, Guid("AE2DE0E4-5BCA-4F2D-AA46-5D13F8FDB3A9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPart
    {
        [PreserveSig] int GetName();
        [PreserveSig] int GetLocalId();
        [PreserveSig] int GetGlobalId();
        [PreserveSig] int GetPartType();
        [PreserveSig] int GetSubType();
        [PreserveSig] int GetControlInterfaceCount();
        [PreserveSig] int GetControlInterface();
        [PreserveSig] int EnumPartsIncoming();
        [PreserveSig] int EnumPartsOutgoing();
        [PreserveSig] int GetTopologyObject(out IDeviceTopology topology);
    }

    [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IKsControl
    {
        [PreserveSig] int KsProperty(ref KsProperty property, uint propertyLength, IntPtr data, uint dataLength, out uint bytesReturned);
    }
}
