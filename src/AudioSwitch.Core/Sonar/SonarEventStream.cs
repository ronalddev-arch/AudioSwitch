using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace AudioSwitch.Core.Sonar;

public sealed record SonarEvent(string Name, JsonNode? Data);

/// <summary>
/// Listens to Sonar's push channel (ws://{sonar}/sock) and reconnects with backoff, re-discovering the address,
/// whenever GG/Sonar restarts. Events are raised on the listener thread.
/// </summary>
public sealed class SonarEventStream(SonarDiscovery discovery, ILogger<SonarEventStream> log)
{
    static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(1), MaxBackoff = TimeSpan.FromSeconds(30);

    /// <summary>Raised after every (re)connect. Sonar does not replay state on connect, so re-read what you need.</summary>
    public event EventHandler? Connected;

    public event EventHandler<SonarEvent>? EventReceived;

    public async Task RunAsync(CancellationToken ct)
    {
        var backoff = MinBackoff;
        string? lastError = null;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var baseAddress = await discovery.GetBaseAddressAsync(ct);
                var socketUri = new UriBuilder(baseAddress) { Scheme = "ws", Path = SonarProtocol.WebSocketRoute }.Uri;
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(socketUri, ct);
                log.LogInformation("Listening to Sonar events at {Uri}", socketUri.ToString());
                backoff = MinBackoff;
                lastError = null;
                Connected?.Invoke(this, EventArgs.Empty);
                await ReceiveAsync(socket, ct);
                log.LogInformation("Sonar closed the event stream");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Log each distinct failure once instead of every retry while GG is down.
                if (ex.Message != lastError) log.LogWarning("Sonar event stream unavailable: {Error}", ex.Message);
                lastError = ex.Message;
                discovery.Invalidate();
            }

            try { await Task.Delay(backoff, ct); }
            catch (OperationCanceledException) { break; }
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
        }
    }

    async Task ReceiveAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var message = new StringBuilder();
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return;
            message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage) continue;

            var text = message.ToString();
            message.Clear();
            Dispatch(text);
        }
    }

    void Dispatch(string text)
    {
        // Skip the high-rate meter stream without parsing it.
        if (text.AsSpan(0, Math.Min(text.Length, 64)).Contains(SonarProtocol.EventVolumeData, StringComparison.Ordinal)) return;

        JsonNode? frame;
        try { frame = JsonNode.Parse(text); }
        catch (System.Text.Json.JsonException ex)
        {
            log.LogDebug("Ignoring unparsable Sonar frame: {Error}", ex.Message);
            return;
        }
        if ((string?)frame?["event"] is not { } name) return;

        try { EventReceived?.Invoke(this, new SonarEvent(name, frame["data"])); }
        catch (Exception ex) { log.LogError(ex, "Handler for Sonar event {Event} failed", name); }
    }
}
