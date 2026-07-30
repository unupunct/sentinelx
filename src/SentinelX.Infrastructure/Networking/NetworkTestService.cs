using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SentinelX.Infrastructure.Networking;

public sealed class NetworkTestService
{
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        // Some sites reject requests without a browser-like user agent
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) SentinelX/1.0");
        return client;
    }

    /// <summary>
    /// Tests whether the platform's website answers over HTTPS and how fast.
    /// Any HTTP response (even 403 bot-blocks) proves connectivity — only transport
    /// failures (DNS, TLS, timeout, reset) count as unreachable.
    /// </summary>
    public async Task<(bool Ok, double Ms, int? Status, string? Error)> HttpsCheckAsync(
        string host, CancellationToken ct)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://" + host + "/");
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();
            return (true, sw.Elapsed.TotalMilliseconds, (int)resp.StatusCode, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            return (false, 0, null, "timed out");
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException?.Message ?? ex.Message;
            return (false, 0, null, msg);
        }
    }

    public async Task<PingSummary> PingHostAsync(
        PingKind kind, string displayName, string host,
        int count, int timeoutMs, int spacingMs,
        CancellationToken ct, IProgress<string>? progress = null)
    {
        var result = new PingSummary { Kind = kind, DisplayName = displayName, Target = host, Sent = count };

        IPAddress? addr;
        if (IPAddress.TryParse(host, out addr))
        {
            result.Resolved = true;
        }
        else
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var addrs = await Dns.GetHostAddressesAsync(host, ct);
                sw.Stop();
                result.DnsMs = sw.Elapsed.TotalMilliseconds;
                addr = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                       ?? addrs.FirstOrDefault();
                result.Resolved = addr is not null;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                result.Error = "DNS lookup failed";
                return result;
            }
        }

        if (addr is null)
        {
            result.Error = "no address found";
            return result;
        }
        result.Address = addr.ToString();

        var rtts = new List<double>(count);
        var lost = 0;
        using var ping = new Ping();
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(addr, timeoutMs);
                if (reply.Status == IPStatus.Success) rtts.Add(reply.RoundtripTime);
                else lost++;
            }
            catch (OperationCanceledException) { throw; }
            catch { lost++; }

            progress?.Report($"Pinging {displayName} ({i + 1}/{count})...");
            if (i < count - 1) await Task.Delay(spacingMs, ct);
        }

        result.Lost = lost;
        if (rtts.Count > 0)
        {
            result.AvgMs = rtts.Average();
            result.MinMs = rtts.Min();
            result.MaxMs = rtts.Max();
            // Jitter: mean absolute difference of consecutive RTTs (RFC 3550 style)
            if (rtts.Count > 1)
            {
                double sum = 0;
                for (var i = 1; i < rtts.Count; i++) sum += Math.Abs(rtts[i] - rtts[i - 1]);
                result.JitterMs = sum / (rtts.Count - 1);
            }
        }
        return result;
    }

    public async Task<(bool Ok, double Ms, string? Error)> TcpConnectAsync(
        string host, int port, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            var sw = Stopwatch.StartNew();
            await client.ConnectAsync(host, port, cts.Token);
            sw.Stop();
            return (true, sw.Elapsed.TotalMilliseconds, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (false, 0, "timeout");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return (false, 0, ex.Message);
        }
    }

    public static string? GetDefaultGateway()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var gw = ni.GetIPProperties().GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
                if (gw is not null && !gw.Equals(IPAddress.Any)) return gw.ToString();
            }
        }
        catch { }
        return null;
    }

    public static bool TryParseRtmpUrl(string url, out string host, out int port)
    {
        host = "";
        port = 1935;
        if (string.IsNullOrWhiteSpace(url)) return false;
        url = url.Trim();
        if (!url.Contains("://")) url = "rtmp://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)) return false;
        host = uri.Host;
        port = uri.Port > 0 ? uri.Port
            : uri.Scheme.Equals("rtmps", StringComparison.OrdinalIgnoreCase) ? 443 : 1935;
        return true;
    }
}
