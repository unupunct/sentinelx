namespace SentinelX.Infrastructure.Networking;

public sealed class SpeedTestResult
{
    public double? DownloadMbps { get; set; }
    public double? DownloadMinSustainedMbps { get; set; }
    public double? UploadMbps { get; set; }
    public double? UploadMinSustainedMbps { get; set; }

    /// <summary>Ping to 1.1.1.1 with the line idle, measured just before the test.</summary>
    public double? IdlePingMs { get; set; }
    /// <summary>Median ping to 1.1.1.1 while the upload was saturated (bufferbloat probe).</summary>
    public double? LoadedPingMs { get; set; }
    /// <summary>Latency increase under upload load; the classic cause of mid-stream stutters.</summary>
    public double? BufferbloatMs =>
        IdlePingMs is not null && LoadedPingMs is not null
            ? Math.Max(0, LoadedPingMs.Value - IdlePingMs.Value)
            : null;

    public string? Error { get; set; }
}

public sealed record SpeedProgress(string Phase, double CurrentMbps, double ElapsedSec, double TotalSec);
