using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace AudioSwitch.Core.Sonar;

public sealed class SonarClient(SonarDiscovery discovery, ILogger<SonarClient> log) : ISonarClient, IDisposable
{
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public async Task<SonarMode> GetModeAsync(CancellationToken ct = default) =>
        SonarProtocol.ParseMode(await GetRawModeAsync(ct));

    public async Task<SonarRouting> GetRoutingAsync(CancellationToken ct = default)
    {
        var rawMode = await GetRawModeAsync(ct) ?? "";
        var mode = SonarProtocol.ParseMode(rawMode);
        var devices = mode == SonarMode.Stream
            ? SonarProtocol.ParseStreamRedirections(await GetJsonAsync(SonarProtocol.StreamRedirectionsRoute, ct))
            : SonarProtocol.ParseRedirections(await GetJsonAsync(SonarProtocol.ClassicRedirectionsRoute, ct));
        return new SonarRouting(mode, devices, rawMode);
    }

    public async Task SetDeviceAsync(SonarMode mode, SonarChannel channel, string deviceId, CancellationToken ct = default)
    {
        var route = SonarProtocol.SetRedirectionRoute(mode, channel, deviceId);
        await SendAsync(async b =>
        {
            using var response = await _http.PutAsync(new Uri(b, route), content: null, ct);
            if (!response.IsSuccessStatusCode)
                throw new SonarApiException(response.StatusCode, route, await response.Content.ReadAsStringAsync(ct));
            return true;
        }, ct);
    }

    public async Task<IReadOnlyDictionary<string, bool>> GetWirelessStatesAsync(CancellationToken ct = default) =>
        SonarProtocol.ParseWirelessStates(await GetJsonAsync(SonarProtocol.FallbackListsRoute, ct));

    Task<string?> GetRawModeAsync(CancellationToken ct) =>
        SendAsync(b => _http.GetFromJsonAsync<string>(new Uri(b, SonarProtocol.ModeRoute), ct), ct);

    async Task<JsonNode> GetJsonAsync(string route, CancellationToken ct) =>
        await SendAsync(b => _http.GetFromJsonAsync<JsonNode>(new Uri(b, route), ct), ct)
        ?? throw new SonarApiException(System.Net.HttpStatusCode.NoContent, route, "empty response");

    /// <summary>Runs a request against the current Sonar address; on a connection-level failure (Sonar restarted on
    /// another port) re-discovers the address and retries once.</summary>
    async Task<T> SendAsync<T>(Func<Uri, Task<T>> request, CancellationToken ct)
    {
        var baseAddress = await discovery.GetBaseAddressAsync(ct);
        try
        {
            return await request(baseAddress);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            log.LogInformation("Sonar not reachable at {Address} ({Error}); re-discovering", baseAddress.ToString(), ex.Message);
            discovery.Invalidate();
            baseAddress = await discovery.GetBaseAddressAsync(ct);
            try
            {
                return await request(baseAddress);
            }
            catch (Exception retryEx) when (IsConnectionFailure(retryEx, ct))
            {
                discovery.Invalidate();
                throw new SonarUnavailableException($"Sonar not reachable at {baseAddress}: {retryEx.Message}", retryEx);
            }
        }
    }

    static bool IsConnectionFailure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException { StatusCode: null } || (ex is TaskCanceledException && !ct.IsCancellationRequested);

    public void Dispose() => _http.Dispose();
}
