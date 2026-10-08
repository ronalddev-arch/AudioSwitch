namespace AudioSwitch.Core.Audio;

[Flags]
public enum OutputRoles
{
    /// <summary>The "default device" (Windows console + multimedia roles).</summary>
    Playback = 1,
    /// <summary>The "default communications device" (calls, Discord, Teams).</summary>
    Communications = 2,
    All = Playback | Communications,
}

public interface IWindowsAudio : IDisposable
{
    /// <summary>
    /// Raised for any endpoint add/remove/state change or default-device change, with a short description.
    /// Raised on a Windows audio worker thread: handlers must return quickly and must not call back into this
    /// interface synchronously.
    /// </summary>
    event EventHandler<string>? Changed;

    IReadOnlyList<AudioEndpoint> GetActiveEndpoints();

    /// <summary>The default playback endpoint id (console role), or null when there is none.</summary>
    string? GetDefaultOutputId();

    /// <summary>Makes the endpoint the default playback device for the given roles.</summary>
    void SetDefaultOutput(string endpointId, OutputRoles roles);

    /// <summary>The default recording endpoint id (console role), or null when there is none.</summary>
    string? GetDefaultInputId();

    /// <summary>Makes the endpoint the default recording device for every role (default and communications). Used in
    /// Windows-only mode; with Sonar the recording default stays on Sonar's virtual microphone.</summary>
    void SetDefaultInput(string endpointId);

    /// <summary>Disconnects a Bluetooth device's audio (all its endpoints) from this PC; it stays paired.
    /// Returns the endpoints that were disconnected.</summary>
    IReadOnlyList<AudioEndpoint> DisconnectBluetooth(AudioEndpoint endpoint);
}
