using System.Text.Json.Nodes;

namespace AudioSwitch.Core.Sonar;

/// <summary>
/// Everything that depends on SteelSeries' unofficial, reverse-engineered Sonar API lives in this file, so a GG update
/// that moves things only needs this file patched. Verified against GG 120.0.0 / Sonar 1.103.
/// </summary>
internal static class SonarProtocol
{
    // --- Discovery: coreProps.json -> GG HTTPS server -> subApps -> sonar.metadata.webServerAddress
    public static readonly IReadOnlyList<string> CorePropsPaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SteelSeries", "SteelSeries Engine 3", "coreProps.json"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SteelSeries", "GG", "coreProps.json"),
    ];
    public const string CorePropsGgAddressKey = "ggEncryptedAddress";
    public const string SubAppsRoute = "subApps";

    // --- REST routes, relative to the Sonar base address
    public const string ModeRoute = "mode"; // a bare JSON string: "classic" or "stream"
    public const string ClassicMode = "classic";
    public const string StreamMode = "stream";
    public const string ClassicRedirectionsRoute = "classicRedirections";
    public const string StreamRedirectionsRoute = "streamRedirections";
    public const string FallbackListsRoute = "fallbackSettings/lists";
    public const string WebSocketRoute = "sock";

    // DataNext27's and SonarTray's clients read/write "stream" (both switched modes live in 2026); one third-party doc
    // says "streamer", so accept that too. Only "classic" has been seen live.
    public static SonarMode ParseMode(string? mode) => mode switch
    {
        ClassicMode => SonarMode.Classic,
        StreamMode or "streamer" => SonarMode.Stream,
        _ => SonarMode.Unknown,
    };

    /// <summary>The PUT that points <paramref name="channel"/> at a device in <paramref name="mode"/>. Classic mode
    /// (and an unknown mode) uses classicRedirections, stream mode uses streamRedirections, which has its own mic.</summary>
    public static string SetRedirectionRoute(SonarMode mode, SonarChannel channel, string deviceId) => mode == SonarMode.Stream
        ? $"{StreamRedirectionsRoute}/{StreamRedirectionKey(channel)}/deviceId/{Uri.EscapeDataString(deviceId)}"
        : $"{ClassicRedirectionsRoute}/{RedirectionKey(channel)}/deviceId/{Uri.EscapeDataString(deviceId)}";

    // Classic redirections use "chat"/"mic"; volume routes, audioDevices.role and the stream mixes' status list use
    // "chatRender"/"chatCapture".
    public static string RedirectionKey(SonarChannel channel) => channel switch
    {
        SonarChannel.Game => "game",
        SonarChannel.Chat => "chat",
        SonarChannel.Media => "media",
        SonarChannel.Aux => "aux",
        SonarChannel.Mic => "mic",
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "not a classic-mode redirection"),
    };

    // streamRedirectionId values: "monitoring" (personal mix), "streaming" (stream mix, never touched) and "mic".
    public static string StreamRedirectionKey(SonarChannel channel) => channel switch
    {
        SonarChannel.Monitoring => "monitoring",
        SonarChannel.Mic => "mic",
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "not a stream-mode redirection"),
    };

    // --- WebSocket events: frames are {"event": name, "data": ...}
    public const string EventFallbackUpdated = "SONAR_EVENT_FALLBACK_UPDATED";
    public const string EventRedirectionStatusUpdate = "SONAR_EVENT_REDIRECTION_STATUS_UPDATE";
    public const string EventDeviceStatusUpdate = "SONAR_EVENT_DEVICE_STATUS_UPDATE";
    public const string EventVolumeData = "SONAR_EVENT_VOLUME_DATA"; // high-rate level meters

    // --- Parsing

    public static Uri ParseSonarAddress(JsonNode subApps)
    {
        var sonar = subApps["subApps"]?["sonar"]
            ?? throw new SonarUnavailableException("GG reports no Sonar sub-app.", presence: SonarPresence.Disabled);
        // isEnabled = false: the user turned Sonar off in GG (assumed meaning; not seen live). isRunning/isReady = false:
        // Sonar is (re)starting.
        if (sonar["isEnabled"]?.GetValue<bool>() != true)
            throw new SonarUnavailableException("Sonar is turned off in GG (subApps.sonar.isEnabled = false).", presence: SonarPresence.Disabled);
        foreach (var flag in new[] { "isRunning", "isReady" })
            if (sonar[flag]?.GetValue<bool>() != true)
                throw new SonarUnavailableException($"Sonar is not available yet (subApps.sonar.{flag} = false).", presence: SonarPresence.Starting);

        var address = (string?)sonar["metadata"]?["webServerAddress"];
        if (string.IsNullOrWhiteSpace(address))
            throw new SonarUnavailableException("Sonar has no webServerAddress.");
        return new Uri(address.TrimEnd('/') + "/");
    }

    public static IReadOnlyDictionary<SonarChannel, string> ParseRedirections(JsonNode classicRedirections)
    {
        var result = new Dictionary<SonarChannel, string>();
        foreach (var item in classicRedirections.AsArray())
        {
            var key = (string?)item?["id"];
            var deviceId = (string?)item?["deviceId"];
            if (key is null || deviceId is null) continue;
            foreach (var channel in SonarChannels.ClassicOutputs.Append(SonarChannel.Mic))
                if (RedirectionKey(channel) == key)
                    result[channel] = deviceId;
        }
        return result;
    }

    /// <summary>
    /// From streamRedirections: the personal mix's device as <see cref="SonarChannel.Monitoring"/> and the stream-mode
    /// mic. Entries look like {streamRedirectionId, deviceId, status: [{role, isEnabled}], isRunning}; the stream mix
    /// ("streaming") is skipped. A redirection that was never set up reads as "" (seen live); presumably Sonar also
    /// clears it like a classic one when its device disappears.
    /// </summary>
    public static IReadOnlyDictionary<SonarChannel, string> ParseStreamRedirections(JsonNode streamRedirections)
    {
        var result = new Dictionary<SonarChannel, string>();
        foreach (var item in streamRedirections.AsArray())
        {
            var key = (string?)item?["streamRedirectionId"];
            var deviceId = (string?)item?["deviceId"];
            if (key is null || deviceId is null) continue;
            foreach (var channel in SonarChannels.StreamOutputs.Append(SonarChannel.Mic))
                if (StreamRedirectionKey(channel) == key)
                    result[channel] = deviceId;
        }
        return result;
    }

    /// <summary>
    /// From fallbackSettings/lists (or a FALLBACK_UPDATED event, which may contain only some channels): for every
    /// endpoint backed by a SteelSeries wireless device, whether the wireless headset is currently connected (on).
    /// </summary>
    public static Dictionary<string, bool> ParseWirelessStates(JsonNode fallbackLists)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, list) in fallbackLists.AsObject())
        {
            if (list is not JsonArray entries) continue;
            foreach (var entry in entries)
            {
                if (entry?["isSteelseriesWirelessSupported"]?.GetValue<bool>() != true) continue;
                var id = (string?)entry["id"];
                if (id is null) continue;
                var connected = entry["isSteelseriesWirelessConnected"]?.GetValue<bool>() == true;
                result[id] = result.GetValueOrDefault(id) || connected;
            }
        }
        return result;
    }
}
