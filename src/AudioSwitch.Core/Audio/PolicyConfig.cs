using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace AudioSwitch.Core.Audio;

/// <summary>
/// Undocumented IPolicyConfig (Windows 7+), which the Sound control panel uses to set default endpoints.
/// Verified on Windows 10 19045 without admin rights.
/// </summary>
internal static class PolicyConfig
{
    public static void SetDefaultEndpoint(string deviceId, Role role)
    {
        var policy = (IPolicyConfig)new CPolicyConfigClient();
        try { Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, role)); }
        finally { Marshal.ReleaseComObject(policy); }
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    class CPolicyConfigClient;

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        // Only SetDefaultEndpoint is used; the preceding slots keep the vtable order.
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, Role role);
    }
}
