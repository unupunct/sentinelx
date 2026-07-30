using System.Diagnostics;
using System.Net.NetworkInformation;

namespace SentinelX.Infrastructure.Networking;

public sealed class StabilityResult
{
    public int DurationSec { get; set; }
    public int TargetKbps { get; set; }
    public int PingCount { get; set; }
    public int PingTimeouts { get; set; }
    public int PingSpikes { get; set; }
    public double AvgPingMs { get; set; }
    public double MaxPingMs { get; set; }
    /// <summary>How much of the target upload rate was actually achieved (100 = kept up fully).</summary>
    public double TargetAchievedPct { get; set; }
    /// <summary>Seconds where the upload fell behind the target pace.</summary>
    public double StallSeconds { get; set; }
    public string? Error { get; set; }

    public Verdict Verdict =>
        Error is not null ? Verdict.Red
        : PingTimeouts > 0 || StallSeconds > 5 || TargetAchievedPct < 90 ? Verdict.Red
        : PingSpikes > PingCount * 0.02 || StallSeconds > 0 ? Verdict.Yellow
        : Verdict.Green;
}

/// <summary>
/// Soak test: uploads at a steady stream-like bitrate for several minutes while pinging
/// once per second - catches the intermittent dips that a 10-second speed test misses.
/// </summary>
public sealed class StabilityTestService
{
    private const string UpUrl = "https://speed.cloudflare.com/__up";
    private const double SpikeThresholdMs = 150;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<StabilityResult> RunAsync(
        int durationSec, int targetKbps,
        Action<double?> onPing, Action<string> onStatus,
        CancellationToken ct)
    {
        var result = new StabilityResult { DurationSec = durationSec, TargetKbps = targetKbps };
        var pings = new List<double>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sw = Stopwatch.StartNew();

        var pingerTask = Task.Run(async () =>
        {
            using var ping = new Ping();
            var addr = System.Net.IPAddress.Parse("1.1.1.1");
            while (!cts.Token.IsCancellationRequested && sw.Elapsed.TotalSeconds < durationSec)
            {
                var tickStart = sw.Elapsed;
                double? rtt = null;
                try
                {
                    var reply = await ping.SendPingAsync(addr, 1000);
                    if (reply.Status == IPStatus.Success) rtt = reply.RoundtripTime;
                }
                catch { }

                result.PingCount++;
                if (rtt is null) result.PingTimeouts++;
                else
                {
                    pings.Add(rtt.Value);
                    if (rtt.Value > SpikeThresholdMs) result.PingSpikes++;
                }
                onPing(rtt);

                var wait = TimeSpan.FromSeconds(1) - (sw.Elapsed - tickStart);
                if (wait > TimeSpan.Zero)
                {
                    try { await Task.Delay(wait, cts.Token); } catch { return; }
                }
            }
        }, cts.Token);

        var uploaderTask = Task.Run(async () =>
        {
            var bytesPerSecond = targetKbps * 125; // kbps -> bytes/s
            var chunk = new byte[bytesPerSecond];
            Random.Shared.NextBytes(chunk);
            long sentBytes = 0;
            var consecutiveFailures = 0;

            while (!cts.Token.IsCancellationRequested && sw.Elapsed.TotalSeconds < durationSec)
            {
                var postStart = sw.Elapsed;
                try
                {
                    using var content = new ByteArrayContent(chunk);
                    using var resp = await Http.PostAsync(UpUrl, content, cts.Token);
                    sentBytes += chunk.Length;
                    consecutiveFailures = 0;
                }
                catch (OperationCanceledException) { return; }
                catch
                {
                    if (++consecutiveFailures >= 5)
                    {
                        result.Error = "Upload kept failing during the test (connection dropped?).";
                        return;
                    }
                }

                var elapsed = (sw.Elapsed - postStart).TotalSeconds;
                if (elapsed > 1.5) result.StallSeconds += elapsed - 1.0;
                if (elapsed < 1.0)
                {
                    try { await Task.Delay(TimeSpan.FromSeconds(1.0 - elapsed), cts.Token); } catch { return; }
                }
            }

            var expected = (double)targetKbps * 125 * Math.Min(durationSec, sw.Elapsed.TotalSeconds);
            result.TargetAchievedPct = expected > 0 ? Math.Min(100, sentBytes / expected * 100.0) : 0;
        }, cts.Token);

        var statusTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested && sw.Elapsed.TotalSeconds < durationSec)
            {
                var remaining = TimeSpan.FromSeconds(Math.Max(0, durationSec - sw.Elapsed.TotalSeconds));
                onStatus($"Testing... {remaining:mm\\:ss} remaining " +
                         $"({result.PingTimeouts} timeouts, {result.PingSpikes} spikes so far)");
                try { await Task.Delay(1000, cts.Token); } catch { return; }
            }
        }, cts.Token);

        try
        {
            await Task.WhenAll(pingerTask, uploaderTask, statusTask);
        }
        finally
        {
            cts.Cancel();
        }

        if (pings.Count > 0)
        {
            result.AvgPingMs = pings.Average();
            result.MaxPingMs = pings.Max();
        }
        return result;
    }
}
