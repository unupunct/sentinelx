namespace SentinelX.Infrastructure.Networking;

public enum PingKind
{
    Gateway,
    Internet,
    Ingest
}

public sealed class PingSummary
{
    public PingKind Kind { get; set; }
    public string DisplayName { get; set; } = "";
    public string Target { get; set; } = "";
    public string? Address { get; set; }
    public bool Resolved { get; set; }
    public double? DnsMs { get; set; }
    public int Sent { get; set; }
    public int Lost { get; set; }
    public double LossPct => Sent == 0 ? 0 : Lost * 100.0 / Sent;
    public double AvgMs { get; set; }
    public double MinMs { get; set; }
    public double MaxMs { get; set; }
    public double JitterMs { get; set; }
    public bool AllLost => Sent > 0 && Lost >= Sent;

    // Server reachability test — RTMP port or HTTPS website; null when not applicable
    public bool? TcpOk { get; set; }
    public double? TcpMs { get; set; }
    public int? TcpPort { get; set; }
    /// <summary>True when the reachability test was an HTTPS website request (token/browser platforms).</summary>
    public bool IsHttpsCheck { get; set; }
    public int? HttpStatus { get; set; }

    public string? Error { get; set; }
    public Verdict Verdict { get; set; } = Verdict.Info;

    public string PingText => Error is not null ? Error
        : AllLost ? "no reply"
        : $"{AvgMs:0} ms avg ({MinMs:0}-{MaxMs:0})";
    public string LossText => Error is not null ? "-" : $"{LossPct:0.#}%";
    public string JitterText => Error is not null || AllLost ? "-" : $"{JitterMs:0.#} ms";
    public string TcpText => TcpOk is null ? "-"
        : IsHttpsCheck
            ? (TcpOk == true ? $"website OK (HTTP {HttpStatus}, {TcpMs:0} ms)" : "website FAILED")
            : (TcpOk == true ? $"port {TcpPort} OK ({TcpMs:0} ms)" : $"port {TcpPort} FAILED");
}
