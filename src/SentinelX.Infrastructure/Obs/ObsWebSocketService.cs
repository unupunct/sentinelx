using System.Diagnostics;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SentinelX.Infrastructure.Obs;

public sealed class ObsLiveStatus
{
    public bool Connected { get; init; }
    public bool Streaming { get; init; }
    public bool Reconnecting { get; init; }
    public double CongestionPct { get; init; }
    public long SkippedFrames { get; init; }
    public long TotalFrames { get; init; }
    public double SkippedPct => TotalFrames > 0 ? SkippedFrames * 100.0 / TotalFrames : 0;
    public double KbpsOut { get; init; }
    public string DurationText { get; init; } = "";
    /// <summary>Set when there is no live data to show (OBS closed, WebSocket off, ...).</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Minimal obs-websocket v5 client. Reads the port/password straight from OBS's own
/// config so the operator never types anything; polls GetStreamStatus for live
/// dropped-frames/congestion/bitrate while the stream is running. Read-only: this
/// class never calls anything beyond GetStreamStatus, so it can never change an OBS setting.
/// </summary>
public sealed class ObsWebSocketService : IDisposable
{
    private sealed record WsConfig(bool Enabled, int Port, bool AuthRequired, string Password);

    private ClientWebSocket? _ws;
    private long _lastBytes = -1;
    private DateTime _lastBytesAt;
    private int _reqId;

    public async Task<ObsLiveStatus> GetStatusAsync(CancellationToken ct)
    {
        if (Process.GetProcessesByName("obs64").Length == 0 &&
            Process.GetProcessesByName("obs32").Length == 0)
        {
            Disconnect();
            return new ObsLiveStatus { Message = "OBS is not running." };
        }

        var cfg = ReadConfig();
        if (cfg is null)
        {
            return new ObsLiveStatus
            {
                Message = "Live OBS data needs the WebSocket server: in OBS, Tools > WebSocket Server Settings > 'Enable WebSocket server' > OK."
            };
        }
        if (!cfg.Enabled)
        {
            return new ObsLiveStatus
            {
                Message = "OBS WebSocket server is disabled. In OBS: Tools > WebSocket Server Settings > check 'Enable WebSocket server' > OK. This app then shows live stream health here."
            };
        }

        try
        {
            if (_ws is not { State: WebSocketState.Open })
                await ConnectAsync(cfg, ct);
            return await QueryStreamStatusAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Disconnect();
            return new ObsLiveStatus { Message = "Cannot read live data from OBS (" + ex.Message + ")" };
        }
    }

    private static WsConfig? ReadConfig()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "obs-studio", "plugin_config", "obs-websocket", "config.json");
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            return new WsConfig(
                root.TryGetProperty("server_enabled", out var en) && en.GetBoolean(),
                root.TryGetProperty("server_port", out var port) ? port.GetInt32() : 4455,
                root.TryGetProperty("auth_required", out var auth) && auth.GetBoolean(),
                root.TryGetProperty("server_password", out var pw) ? pw.GetString() ?? "" : "");
        }
        catch
        {
            return null;
        }
    }

    private async Task ConnectAsync(WsConfig cfg, CancellationToken ct)
    {
        Disconnect();
        _ws = new ClientWebSocket();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(3000);

        await _ws.ConnectAsync(new Uri($"ws://127.0.0.1:{cfg.Port}"), cts.Token);

        using var hello = await ReceiveJsonAsync(cts.Token);
        if (hello.RootElement.GetProperty("op").GetInt32() != 0)
            throw new Exception("unexpected handshake");

        string? authString = null;
        var helloData = hello.RootElement.GetProperty("d");
        if (helloData.TryGetProperty("authentication", out var authInfo))
        {
            var salt = authInfo.GetProperty("salt").GetString() ?? "";
            var challenge = authInfo.GetProperty("challenge").GetString() ?? "";
            authString = BuildAuth(cfg.Password, salt, challenge);
        }

        var identify = authString is null
            ? "{\"op\":1,\"d\":{\"rpcVersion\":1,\"eventSubscriptions\":0}}"
            : $"{{\"op\":1,\"d\":{{\"rpcVersion\":1,\"eventSubscriptions\":0,\"authentication\":\"{authString}\"}}}}";
        await SendAsync(identify, cts.Token);

        using var identified = await ReceiveJsonAsync(cts.Token);
        if (identified.RootElement.GetProperty("op").GetInt32() != 2)
            throw new Exception("authentication failed (check the WebSocket password in OBS)");

        _lastBytes = -1;
    }

    private async Task<ObsLiveStatus> QueryStreamStatusAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(3000);

        var id = (++_reqId).ToString();
        await SendAsync(
            $"{{\"op\":6,\"d\":{{\"requestType\":\"GetStreamStatus\",\"requestId\":\"{id}\"}}}}", cts.Token);

        // Skip any stray messages until our response arrives
        for (var i = 0; i < 10; i++)
        {
            using var msg = await ReceiveJsonAsync(cts.Token);
            var root = msg.RootElement;
            if (root.GetProperty("op").GetInt32() != 7) continue;
            var d = root.GetProperty("d");
            if (d.GetProperty("requestId").GetString() != id) continue;
            if (!d.GetProperty("requestStatus").GetProperty("result").GetBoolean())
                throw new Exception("OBS rejected the status request");

            var r = d.GetProperty("responseData");
            var active = r.GetProperty("outputActive").GetBoolean();
            var bytes = r.TryGetProperty("outputBytes", out var b) ? b.GetInt64() : 0;

            double kbps = 0;
            var now = DateTime.UtcNow;
            if (_lastBytes >= 0 && bytes >= _lastBytes)
            {
                var secs = (now - _lastBytesAt).TotalSeconds;
                if (secs > 0.2) kbps = (bytes - _lastBytes) * 8.0 / secs / 1000.0;
            }
            _lastBytes = bytes;
            _lastBytesAt = now;

            return new ObsLiveStatus
            {
                Connected = true,
                Streaming = active,
                Reconnecting = r.TryGetProperty("outputReconnecting", out var rec) && rec.GetBoolean(),
                CongestionPct = (r.TryGetProperty("outputCongestion", out var cong) ? cong.GetDouble() : 0) * 100.0,
                SkippedFrames = r.TryGetProperty("outputSkippedFrames", out var sk) ? sk.GetInt64() : 0,
                TotalFrames = r.TryGetProperty("outputTotalFrames", out var tf) ? tf.GetInt64() : 0,
                KbpsOut = kbps,
                DurationText = r.TryGetProperty("outputTimecode", out var tc)
                    ? (tc.GetString() ?? "").Split('.')[0] : ""
            };
        }
        throw new Exception("no response from OBS");
    }

    private static string BuildAuth(string password, string salt, string challenge)
    {
        using var sha = SHA256.Create();
        var secret = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    private Task SendAsync(string json, CancellationToken ct)
        => _ws!.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, ct);

    private async Task<JsonDocument> ReceiveJsonAsync(CancellationToken ct)
    {
        var buffer = new byte[16384];
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await _ws!.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new Exception("OBS closed the connection");
            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) break;
        }
        return JsonDocument.Parse(Encoding.UTF8.GetString(ms.ToArray()));
    }

    private void Disconnect()
    {
        try { _ws?.Abort(); } catch { }
        try { _ws?.Dispose(); } catch { }
        _ws = null;
        _lastBytes = -1;
    }

    public void Dispose() => Disconnect();
}
