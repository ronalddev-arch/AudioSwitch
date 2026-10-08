using System.Net;
using AudioSwitch.Core.Audio;
using AudioSwitch.Core.Sonar;
using Microsoft.Extensions.Logging;

namespace AudioSwitch.Core.Routing;

public sealed class RoutingExecutor(IWindowsAudio windows, ISonarClient sonar, ILogger<RoutingExecutor> log)
{
    // Sonar learns about a newly connected device a moment after Windows does and answers 400 "no device found" until then.
    const int SonarAttempts = 5;
    static readonly TimeSpan SonarRetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>Applies as much of the plan as possible; returns the failures (empty = fully applied).</summary>
    public async Task<IReadOnlyList<string>> ApplyAsync(SwitchPlan plan, CancellationToken ct)
    {
        log.LogInformation("Applying: {Plan}. Why: {Reason}", plan, plan.Reason);
        var failures = new List<string>();

        if (plan.WindowsDefaults is { } defaults)
        {
            if (defaults.Default.Id == defaults.Communications.Id)
            {
                SetWindowsDefault(defaults.Default, OutputRoles.All, failures);
            }
            else
            {
                SetWindowsDefault(defaults.Default, OutputRoles.Playback, failures);
                SetWindowsDefault(defaults.Communications, OutputRoles.Communications, failures);
            }
        }

        if (plan.WindowsOnly)
        {
            // No Sonar: the mic is the Windows recording default, and Sonar is never contacted.
            if (plan.Mic is { } windowsMic) SetWindowsDefaultInput(windowsMic, failures);
            return failures;
        }

        IReadOnlyList<SonarChannel> channels = plan.Output is null ? [] : plan.Channels;
        if (channels.Count == 0 && plan.Mic is null) return failures;

        // Read the mode right before writing: a prompt can be answered after Sonar switched modes.
        SonarMode mode;
        try { mode = await sonar.GetModeAsync(ct); }
        catch (Exception ex) when (ex is SonarApiException or SonarUnavailableException)
        {
            log.LogError("Reading Sonar's mode failed, so Sonar is left unchanged: {Error}", ex.Message);
            failures.Add($"Sonar: {ex.Message}");
            return failures;
        }
        var forMode = SonarChannels.ForMode(channels, mode);
        if (!forMode.SequenceEqual(channels))
            log.LogInformation("Sonar is in {Mode} mode: {Planned} → {Channels}", mode.ToString(), string.Join("+", channels), string.Join("+", forMode));

        if (plan.Output is { } target)
            foreach (var channel in forMode)
                await SetSonarAsync(mode, channel, target, failures, ct);

        if (plan.Mic is { } mic)
            await SetSonarAsync(mode, SonarChannel.Mic, mic, failures, ct);

        return failures;
    }

    void SetWindowsDefaultInput(AudioEndpoint mic, List<string> failures)
    {
        try
        {
            windows.SetDefaultInput(mic.Id);
            log.LogInformation("Windows recording default → {Endpoint}", mic.Name);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Setting the Windows recording default to {Endpoint} failed", mic.Name);
            failures.Add($"Windows recording default: {ex.Message}");
        }
    }

    void SetWindowsDefault(AudioEndpoint endpoint, OutputRoles roles, List<string> failures)
    {
        try
        {
            windows.SetDefaultOutput(endpoint.Id, roles);
            log.LogInformation("Windows default ({Roles}) → {Endpoint}", roles.ToString(), endpoint.Name);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Setting the Windows default ({Roles}) to {Endpoint} failed", roles.ToString(), endpoint.Name);
            failures.Add($"Windows default: {ex.Message}");
        }
    }

    async Task SetSonarAsync(SonarMode mode, SonarChannel channel, AudioEndpoint device, List<string> failures, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await sonar.SetDeviceAsync(mode, channel, device.Id, ct);
                log.LogInformation("Sonar {Channel} → {Device}", channel.ToString(), device.Name);
                return;
            }
            catch (SonarApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest && attempt < SonarAttempts)
            {
                log.LogDebug("Sonar rejected {Channel} → {Device} (attempt {Attempt}): {Body}; retrying", channel.ToString(), device.Name, attempt, ex.Body);
                await Task.Delay(SonarRetryDelay, ct);
            }
            catch (Exception ex) when (ex is SonarApiException or SonarUnavailableException)
            {
                log.LogError("Sonar {Channel} → {Device} failed: {Error}", channel.ToString(), device.Name, ex.Message);
                failures.Add($"Sonar {channel}: {ex.Message}");
                return;
            }
        }
    }

    public IReadOnlyList<AudioEndpoint> DisconnectBluetooth(AudioEndpoint device)
    {
        var disconnected = windows.DisconnectBluetooth(device);
        log.LogInformation("Disconnected Bluetooth endpoints: {Endpoints}", string.Join(", ", disconnected.Select(e => e.Name)));
        return disconnected;
    }
}
