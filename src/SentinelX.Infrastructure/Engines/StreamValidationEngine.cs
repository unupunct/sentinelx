using System.Diagnostics;
using SentinelX.Core.Interfaces;
using SentinelX.Core.Models;
using SentinelX.Infrastructure.Networking;

namespace SentinelX.Infrastructure.Engines;

/// <summary>
/// On-demand network/streaming-capacity validation: gateway/internet ping+jitter+loss, DNS,
/// RTMP-ingest reachability for Twitch/YouTube, and a short synthetic upload/download/bufferbloat
/// measurement. This does NOT simulate an actual stream to a real platform — no stream keys are
/// ever handled and no platform account is touched; it's local network validation only, same
/// design as the source project this was ported from. The speed test is intentionally short
/// (2 streams / 4s) to keep a routine "Run Scan" responsive — <see cref="StabilityTestService"/>
/// (the longer soak test) is exposed separately via the app's headless --soak mode rather than
/// run automatically here.
/// </summary>
public sealed class StreamValidationEngine : IDiagnosticEngine
{
    private const double GatewayGreenMs = 5, GatewayYellowMs = 30;
    private const double InternetGreenMs = 30, InternetYellowMs = 80;
    private const double IngestGreenMs = 60, IngestYellowMs = 120;
    private const double JitterGreenMs = 5, JitterYellowMs = 20;
    private const double LossYellowMaxPct = 1;
    private const double DnsGreenMs = 50, DnsYellowMs = 200;
    private const double UploadGreenMbps = 10, UploadYellowMbps = 5;
    private const double BufferbloatGreenMs = 50, BufferbloatYellowMs = 150;

    private static readonly (string Name, string Host, int Port)[] IngestTargets =
    {
        ("Twitch", "live.twitch.tv", 1935),
        ("YouTube", "a.rtmp.youtube.com", 1935)
    };

    private readonly IHealthScoreCalculator _healthScoreCalculator;
    private readonly NetworkTestService _networkTestService = new();
    private readonly SpeedTestService _speedTestService = new();

    public StreamValidationEngine(IHealthScoreCalculator healthScoreCalculator)
    {
        _healthScoreCalculator = healthScoreCalculator;
    }

    public string Id => "stream-validation";
    public string DisplayName => "Streaming Validation";
    public EngineCategory Category => EngineCategory.StreamValidation;

    public async Task<EngineResult> RunAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var collectedAt = DateTimeOffset.Now;
        var findings = new List<Finding>();
        string? error = null;

        try
        {
            var pings = new List<PingSummary>();

            var gateway = NetworkTestService.GetDefaultGateway();
            if (gateway is not null)
            {
                pings.Add(await _networkTestService.PingHostAsync(
                    PingKind.Gateway, "Router", gateway, count: 8, timeoutMs: 1000, spacingMs: 100, cancellationToken));
            }

            foreach (var (name, host) in new[] { ("Cloudflare", "1.1.1.1"), ("Google", "8.8.8.8") })
            {
                pings.Add(await _networkTestService.PingHostAsync(
                    PingKind.Internet, name, host, count: 8, timeoutMs: 1000, spacingMs: 100, cancellationToken));
            }

            foreach (var (name, host, port) in IngestTargets)
            {
                var ping = await _networkTestService.PingHostAsync(
                    PingKind.Ingest, name, host, count: 8, timeoutMs: 1000, spacingMs: 100, cancellationToken);
                var (tcpOk, tcpMs, _) = await _networkTestService.TcpConnectAsync(host, port, 3000, cancellationToken);
                ping.TcpOk = tcpOk;
                ping.TcpMs = tcpMs;
                ping.TcpPort = port;
                pings.Add(ping);
            }

            findings.AddRange(BuildNetworkFindings(pings));

            var speed = await _speedTestService.RunAsync(streams: 2, seconds: 4, progress: null, cancellationToken);
            findings.AddRange(BuildSpeedFindings(speed));
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        stopwatch.Stop();
        var score = error is null ? _healthScoreCalculator.FromFindings(findings) : 0;
        return new EngineResult(EngineCategory.StreamValidation, score, findings, collectedAt, stopwatch.Elapsed, error);
    }

    /// <summary>
    /// ICMP-blocked-but-TCP-reachable counts as healthy, not broken — many ingest servers/CDNs
    /// block ping but are fully reachable on the actual streaming port. Ported verbatim from
    /// BestStudioOBSChecker's RecommendationEngine.VerdictForPing rule.
    /// </summary>
    private static bool IsHealthyPing(PingSummary p, double greenMs, double yellowMs, out bool degraded)
    {
        degraded = false;
        if (p.Error is not null || !p.Resolved) return false;
        if (p.AllLost) return p.TcpOk == true;
        if (p.Kind == PingKind.Ingest && p.TcpOk == false) return false;
        if (p.AvgMs > yellowMs || p.LossPct > LossYellowMaxPct || p.JitterMs > JitterYellowMs) return false;
        degraded = p.AvgMs > greenMs || p.LossPct > 0 || p.JitterMs > JitterGreenMs;
        return true;
    }

    private static IEnumerable<Finding> BuildNetworkFindings(List<PingSummary> pings)
    {
        var gateway = pings.FirstOrDefault(p => p.Kind == PingKind.Gateway);
        var gwDegraded = false;
        if (gateway is not null && !IsHealthyPing(gateway, GatewayGreenMs, GatewayYellowMs, out gwDegraded))
        {
            yield return new Finding(
                "Connection to the router itself is slow or failing",
                $"Ping to the router: {gateway.PingText}. This is a problem inside the studio, " +
                "not the wider internet.",
                Severity.Critical,
                0.85,
                "Use a network cable instead of WiFi, try a different cable/port, or restart the router.",
                "High — everything downstream (streaming, calls, browsing) is affected.");
        }
        else if (gwDegraded)
        {
            yield return new Finding(
                "Connection to the router is slower than ideal",
                $"Ping to the router: {gateway!.PingText}.",
                Severity.Warning,
                0.6,
                "Prefer a wired connection over WiFi for the streaming PC.",
                "Low to medium — small added latency, unlikely to be visible alone.");
        }

        var internet = pings.Where(p => p.Kind == PingKind.Internet).ToList();
        var worstInternet = internet.Where(p => p.Error is null && !p.AllLost)
            .OrderByDescending(p => p.AvgMs).FirstOrDefault();
        var netDegraded = false;
        if (worstInternet is not null && !IsHealthyPing(worstInternet, InternetGreenMs, InternetYellowMs, out netDegraded))
        {
            yield return new Finding(
                "General internet latency is high",
                $"{worstInternet.AvgMs:0} ms to {worstInternet.DisplayName}.",
                Severity.Warning,
                0.6,
                "Check with the internet provider; avoid other downloads/updates while streaming.",
                "Medium — adds latency and reduces margin for jitter/loss.");
        }
        else if (internet.Any() && worstInternet is null)
        {
            yield return new Finding(
                "No internet reply from public DNS servers",
                "1.1.1.1 and 8.8.8.8 both failed to respond — the internet connection may be down " +
                "or heavily filtered.",
                Severity.Critical,
                0.7,
                "Check the router and contact the internet provider.",
                "High — indicates the connection may be down entirely.");
        }
        else if (netDegraded)
        {
            yield return new Finding(
                "Internet latency is a little high",
                $"{worstInternet!.AvgMs:0} ms to {worstInternet.DisplayName}.",
                Severity.Info,
                0.5,
                "No action needed unless streaming quality is visibly affected.",
                "Low.");
        }

        var answered = pings.Where(p => p.Error is null && !p.AllLost).ToList();
        if (answered.Count > 0)
        {
            var worstJitter = answered.OrderByDescending(p => p.JitterMs).First();
            if (worstJitter.JitterMs > JitterGreenMs)
            {
                yield return new Finding(
                    "Connection speed is fluctuating (jitter)",
                    $"{worstJitter.JitterMs:0.#} ms jitter to {worstJitter.DisplayName}. This causes " +
                    "stutters even when average speed looks fine.",
                    worstJitter.JitterMs > JitterYellowMs ? Severity.Critical : Severity.Warning,
                    0.6,
                    "Common causes: WiFi, other devices sharing the connection, or an overloaded " +
                    "provider line.",
                    "Medium to high — a frequent cause of stream stutter.");
            }

            var worstLoss = answered.OrderByDescending(p => p.LossPct).FirstOrDefault(p => p.LossPct > 0);
            if (worstLoss is not null)
            {
                yield return new Finding(
                    "Packet loss detected",
                    $"{worstLoss.LossPct:0.#}% loss to {worstLoss.DisplayName}. Streaming is very " +
                    "sensitive to this — it causes dropped frames and disconnects.",
                    worstLoss.LossPct > LossYellowMaxPct ? Severity.Critical : Severity.Warning,
                    0.6,
                    "Use a cable instead of WiFi; if it persists on a cable, contact the internet provider.",
                    "High — directly causes dropped frames/disconnects.");
            }
        }

        var dnsTimes = pings.Where(p => p.DnsMs is not null).Select(p => p.DnsMs!.Value).ToList();
        if (dnsTimes.Count > 0)
        {
            var worstDns = dnsTimes.Max();
            if (worstDns > DnsGreenMs)
            {
                yield return new Finding(
                    "DNS lookups are slow",
                    $"Slowest DNS lookup took {worstDns:0} ms.",
                    worstDns > DnsYellowMs ? Severity.Warning : Severity.Info,
                    0.5,
                    "Consider setting the network adapter's DNS servers to 1.1.1.1 and 8.8.8.8.",
                    "Low — mostly affects connection setup time, not steady-state streaming.");
            }
        }

        foreach (var ingest in pings.Where(p => p.Kind == PingKind.Ingest))
        {
            if (ingest.TcpOk == false)
            {
                yield return new Finding(
                    $"Cannot reach {ingest.DisplayName}'s streaming ingest",
                    ingest.AllLost
                        ? $"Both ping and the RTMP port ({ingest.TcpPort}) failed for {ingest.DisplayName}."
                        : $"Ping to {ingest.DisplayName} succeeded but the RTMP port ({ingest.TcpPort}) is blocked.",
                    Severity.Critical,
                    0.7,
                    "Check whether a firewall/antivirus or the router is blocking outgoing " +
                    $"connections on port {ingest.TcpPort}.",
                    "High — streaming to this platform will fail entirely.");
            }
        }
    }

    private static IEnumerable<Finding> BuildSpeedFindings(SpeedTestResult speed)
    {
        if (speed.UploadMbps is null)
        {
            yield return new Finding(
                "Upload speed test failed",
                speed.Error ?? "The speed test could not measure upload throughput.",
                Severity.Warning,
                0.5,
                "Check that this PC can reach speed.cloudflare.com and that no firewall blocks it.",
                "Unknown — upload capacity could not be verified.");
            yield break;
        }

        var up = speed.UploadMbps.Value;
        var upMin = speed.UploadMinSustainedMbps ?? up;
        if (upMin < UploadYellowMbps)
        {
            yield return new Finding(
                "Upload speed is too low for reliable streaming",
                $"Measured {up:0.0} Mbps (worst second: {upMin:0.0} Mbps).",
                Severity.Critical,
                0.8,
                "Below ~5 Mbps, HD streaming will constantly drop frames. Contact the internet " +
                "provider about a faster upload plan or lower the target resolution/bitrate.",
                "High — the single most likely cause of dropped frames.");
        }
        else if (upMin < UploadGreenMbps)
        {
            yield return new Finding(
                "Upload speed has little headroom",
                $"Measured {up:0.0} Mbps (worst second: {upMin:0.0} Mbps).",
                Severity.Warning,
                0.7,
                "Works, but leaves little margin. Consider a faster upload plan for higher-bitrate streams.",
                "Medium — fine for modest bitrates, risky for high-bitrate/high-resolution streams.");
        }

        if (speed.BufferbloatMs is { } bloat && bloat > BufferbloatGreenMs)
        {
            yield return new Finding(
                "Latency spikes under upload load (bufferbloat)",
                $"Ping went from {speed.IdlePingMs:0} ms idle to {speed.LoadedPingMs:0} ms while " +
                $"uploading (+{bloat:0} ms). This causes stutters the moment a stream goes live " +
                "even though the raw speed test looks fine.",
                bloat > BufferbloatYellowMs ? Severity.Critical : Severity.Warning,
                0.7,
                "Enable QoS/'Smart Queue'/SQM on the router, and keep the streaming bitrate well " +
                "below the measured upload limit.",
                "High — a classic cause of stutter that only appears once a stream/upload starts.");
        }
    }
}
