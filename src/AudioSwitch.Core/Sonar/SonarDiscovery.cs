using System.Net.Http.Json;
using System.Net.Security;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace AudioSwitch.Core.Sonar;

/// <summary>
/// Finds the Sonar web server address. The port changes whenever Sonar restarts, so callers
/// <see cref="Invalidate"/> after a connection failure and the next call re-discovers.
/// </summary>
public sealed class SonarDiscovery(ILogger<SonarDiscovery> log) : IDisposable
{
    // GG's local HTTPS server uses a self-signed certificate; accept it for loopback addresses only.
    readonly HttpClient _gg = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
            errors == SslPolicyErrors.None || request.RequestUri is { IsLoopback: true },
    })
    { Timeout = TimeSpan.FromSeconds(5) };

    readonly SemaphoreSlim _lock = new(1, 1);
    Uri? _baseAddress;

    public async Task<Uri> GetBaseAddressAsync(CancellationToken ct)
    {
        if (_baseAddress is { } cached) return cached;
        await _lock.WaitAsync(ct);
        try
        {
            if (_baseAddress is null)
            {
                _baseAddress = await DiscoverAsync(ct);
                log.LogInformation("Sonar web server found at {Address}", _baseAddress.ToString());
            }
            return _baseAddress;
        }
        finally { _lock.Release(); }
    }

    public void Invalidate() => _baseAddress = null;

    async Task<Uri> DiscoverAsync(CancellationToken ct)
    {
        var corePropsPath = SonarProtocol.CorePropsPaths.FirstOrDefault(File.Exists)
            ?? throw (SteelSeriesVersions.IsGgInstalled()
                ? new SonarUnavailableException("coreProps.json not found; is SteelSeries GG running?")
                : new SonarUnavailableException("SteelSeries GG is not installed (no uninstall entry, no coreProps.json).",
                    presence: SonarPresence.NotInstalled));

        string ggAddress;
        try
        {
            var coreProps = JsonNode.Parse(await File.ReadAllTextAsync(corePropsPath, ct));
            ggAddress = (string?)coreProps?[SonarProtocol.CorePropsGgAddressKey]
                ?? throw new SonarUnavailableException($"{corePropsPath} has no {SonarProtocol.CorePropsGgAddressKey}.");
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            throw new SonarUnavailableException($"Could not read {corePropsPath}: {ex.Message}", ex);
        }

        try
        {
            var subApps = await _gg.GetFromJsonAsync<JsonNode>($"https://{ggAddress}/{SonarProtocol.SubAppsRoute}", ct)
                ?? throw new SonarUnavailableException("GG returned an empty subApps response.");
            return SonarProtocol.ParseSonarAddress(subApps);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException { InnerException: TimeoutException })
        {
            throw new SonarUnavailableException($"GG is not reachable at {ggAddress}: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        _gg.Dispose();
        _lock.Dispose();
    }
}
