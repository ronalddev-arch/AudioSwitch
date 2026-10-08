namespace AudioSwitch.Core.Sonar;

/// <summary>Sonar's current mode and the physical device behind each redirection of that mode: Game/Chat/Media/Aux/Mic
/// in classic mode, Monitoring (the personal mix)/Mic in stream mode. A cleared redirection is "".
/// <see cref="RawMode"/> is what Sonar answered, for logging an <see cref="SonarMode.Unknown"/> mode.</summary>
public sealed record SonarRouting(SonarMode Mode, IReadOnlyDictionary<SonarChannel, string> Devices, string RawMode = "")
{
    /// <summary>The device the user hears: Game in classic mode, the personal mix in stream mode.</summary>
    public string? Output => Devices.GetValueOrDefault(SonarChannels.OutputsFor(Mode)[0]);

    public string? Mic => Devices.GetValueOrDefault(SonarChannel.Mic);
}

/// <summary>The app's view of Sonar. Implementations may throw <see cref="SonarUnavailableException"/>
/// (GG/Sonar not running) or <see cref="SonarApiException"/> (request rejected).</summary>
public interface ISonarClient
{
    Task<SonarMode> GetModeAsync(CancellationToken ct = default);

    /// <summary>The mode and the redirections of that mode.</summary>
    Task<SonarRouting> GetRoutingAsync(CancellationToken ct = default);

    /// <summary>Points <paramref name="channel"/> at a device, using <paramref name="mode"/>'s redirections (the mic
    /// has a separate device per mode). The channel must belong to the mode (<see cref="SonarChannels.OutputsFor"/>).</summary>
    Task SetDeviceAsync(SonarMode mode, SonarChannel channel, string deviceId, CancellationToken ct = default);

    /// <summary>Endpoint id → whether its SteelSeries wireless headset is connected (powered on). Endpoints that are
    /// not SteelSeries wireless devices are absent.</summary>
    Task<IReadOnlyDictionary<string, bool>> GetWirelessStatesAsync(CancellationToken ct = default);
}
