using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace SentinelX.Infrastructure.Networking;

/// <summary>
/// Measures download/upload throughput against Cloudflare's speed test endpoints
/// (anycast — hits the nearest POP, which approximates real first-hop internet capacity).
/// </summary>
public sealed class SpeedTestService
{
    // 100 MB gets rejected with 403 by Cloudflare; 50 MB per request is accepted
    // (the worker loops requests until the measurement window ends)
    private const string DownUrl = "https://speed.cloudflare.com/__down?bytes=50000000";
    private const string UpUrl = "https://speed.cloudflare.com/__up";
    private const double WarmupSec = 1.5;

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        MaxConnectionsPerServer = 16,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public async Task<SpeedTestResult> RunAsync(
        int streams, int seconds, IProgress<SpeedProgress>? progress, CancellationToken ct)
    {
        var result = new SpeedTestResult();

        // Idle latency baseline for the bufferbloat check
        try { result.IdlePingMs = await MeasurePingAsync(6, 250, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { /* baseline is best-effort */ }

        try
        {
            (result.DownloadMbps, result.DownloadMinSustainedMbps) =
                await MeasureAsync(upload: false, streams, seconds, progress, ct, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            result.Error = "Download test failed: " + ex.Message;
        }

        try
        {
            // Ping concurrently while the upload is saturated: latency spikes here are
            // bufferbloat, the classic cause of stutters the moment OBS goes live.
            var loadedPings = new List<double>();
            (result.UploadMbps, result.UploadMinSustainedMbps) =
                await MeasureAsync(upload: true, streams, seconds, progress, ct, loadedPings);
            if (loadedPings.Count >= 3)
            {
                loadedPings.Sort();
                result.LoadedPingMs = loadedPings[loadedPings.Count / 2]; // median
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            result.Error = (result.Error is null ? "" : result.Error + " ") + "Upload test failed: " + ex.Message;
        }
        return result;
    }

    private static async Task<double> MeasurePingAsync(int count, int spacingMs, CancellationToken ct)
    {
        using var ping = new System.Net.NetworkInformation.Ping();
        var addr = System.Net.IPAddress.Parse("1.1.1.1");
        var rtts = new List<double>(count);
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(addr, 1000);
                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                    rtts.Add(reply.RoundtripTime);
            }
            catch (OperationCanceledException) { throw; }
            catch { }
            if (i < count - 1) await Task.Delay(spacingMs, ct);
        }
        if (rtts.Count == 0) throw new Exception("no ping replies");
        return rtts.Average();
    }

    private static async Task PingProbeWorker(List<double> sink, CancellationToken ct)
    {
        using var ping = new System.Net.NetworkInformation.Ping();
        var addr = System.Net.IPAddress.Parse("1.1.1.1");
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var reply = await ping.SendPingAsync(addr, 1500);
                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                {
                    lock (sink) sink.Add(reply.RoundtripTime);
                }
            }
            catch (OperationCanceledException) { return; }
            catch { }
            try { await Task.Delay(500, ct); } catch { return; }
        }
    }

    private static async Task<(double AvgMbps, double MinSustainedMbps)> MeasureAsync(
        bool upload, int streams, int seconds, IProgress<SpeedProgress>? progress, CancellationToken outerCt,
        List<double>? pingSink)
    {
        long total = 0;
        void Report(int n) => Interlocked.Add(ref total, n);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        var sw = Stopwatch.StartNew();
        var totalSec = seconds + WarmupSec;
        var phase = upload ? "Upload" : "Download";

        var workers = Enumerable.Range(0, streams)
            .Select(_ => upload ? UploadWorker(Report, cts.Token) : DownloadWorker(Report, cts.Token))
            .ToList();
        if (pingSink is not null) workers.Add(PingProbeWorker(pingSink, cts.Token));

        var samples = new List<(double T, long Bytes)> { (0, 0) };
        while (sw.Elapsed.TotalSeconds < totalSec)
        {
            await Task.Delay(250, outerCt);
            samples.Add((sw.Elapsed.TotalSeconds, Interlocked.Read(ref total)));
            if (progress is not null && samples.Count >= 2)
            {
                var a = samples[Math.Max(0, samples.Count - 5)];
                var b = samples[^1];
                var current = b.T > a.T ? (b.Bytes - a.Bytes) * 8.0 / (b.T - a.T) / 1_000_000.0 : 0;
                progress.Report(new SpeedProgress(phase, current, b.T, totalSec));
            }
        }
        cts.Cancel();
        try { await Task.WhenAll(workers); } catch { /* workers swallow their own cancellation */ }

        var warm = samples.Where(s => s.T >= WarmupSec).ToList();
        if (warm.Count < 4)
            throw new Exception("no data received (endpoint unreachable?)");

        var first = warm[0];
        var last = warm[^1];
        var avgMbps = (last.Bytes - first.Bytes) * 8.0 / (last.T - first.T) / 1_000_000.0;
        if (avgMbps <= 0.01)
            throw new Exception("no throughput measured (endpoint unreachable or blocked?)");

        // Worst 1-second bucket (4 samples at 250 ms) = minimum sustained rate
        var minSustained = double.MaxValue;
        for (var i = 0; i + 4 < warm.Count; i += 4)
        {
            var a = warm[i];
            var b = warm[i + 4];
            if (b.T <= a.T) continue;
            minSustained = Math.Min(minSustained, (b.Bytes - a.Bytes) * 8.0 / (b.T - a.T) / 1_000_000.0);
        }
        if (minSustained == double.MaxValue) minSustained = avgMbps;

        return (avgMbps, Math.Max(0, minSustained));
    }

    private static async Task DownloadWorker(Action<int> report, CancellationToken ct)
    {
        var buf = new byte[65536];
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var resp = await Http.GetAsync(DownUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                await using var s = await resp.Content.ReadAsStreamAsync(ct);
                int n;
                while ((n = await s.ReadAsync(buf, ct)) > 0)
                {
                    report(n);
                    failures = 0;
                }
            }
            catch (OperationCanceledException) { return; }
            catch
            {
                if (++failures >= 3 || ct.IsCancellationRequested) return;
                try { await Task.Delay(200, ct); } catch { return; }
            }
        }
    }

    private static async Task UploadWorker(Action<int> report, CancellationToken ct)
    {
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Fixed-size POSTs repeated until the deadline cancels us mid-request;
                // bytes are counted as they are written to the request stream.
                using var content = new CountingContent(8_000_000, report, ct);
                using var resp = await Http.PostAsync(UpUrl, content, ct);
                failures = 0;
            }
            catch (OperationCanceledException) { return; }
            catch
            {
                if (++failures >= 3 || ct.IsCancellationRequested) return;
                try { await Task.Delay(200, ct); } catch { return; }
            }
        }
    }

    private sealed class CountingContent : HttpContent
    {
        private static readonly byte[] Chunk = CreateChunk();
        private readonly long _length;
        private readonly Action<int> _report;
        private readonly CancellationToken _ct;

        private static byte[] CreateChunk()
        {
            var b = new byte[65536];
            Random.Shared.NextBytes(b);
            return b;
        }

        public CountingContent(long length, Action<int> report, CancellationToken ct)
        {
            _length = length;
            _report = report;
            _ct = ct;
            Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            long sent = 0;
            while (sent < _length)
            {
                _ct.ThrowIfCancellationRequested();
                var n = (int)Math.Min(Chunk.Length, _length - sent);
                await stream.WriteAsync(Chunk.AsMemory(0, n), _ct);
                sent += n;
                _report(n);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }
    }
}
