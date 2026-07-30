namespace SentinelX.Infrastructure.Obs;

public enum ObsState
{
    NotInstalled,
    InstalledNotConfigured,
    Configured
}

public sealed class ObsAudit
{
    public ObsState State { get; set; }
    public string? InstallPath { get; set; }
    public string? ConfigRoot { get; set; }
    public string? ProfileName { get; set; }
    public string OutputMode { get; set; } = "Simple";
    public string? EncoderId { get; set; }
    public int VBitrateKbps { get; set; } = 2500;
    public bool VBitrateIsDefault { get; set; } = true;
    public int ABitrateKbps { get; set; } = 160;
    public bool ABitrateIsDefault { get; set; } = true;
    /// <summary>Keyframe interval in seconds; null or 0 = auto.</summary>
    public int? KeyintSec { get; set; }
    public bool KeyintIsAuto => KeyintSec is null or 0;
    public int BaseCX { get; set; }
    public int BaseCY { get; set; }
    public int OutCX { get; set; }
    public int OutCY { get; set; }
    public double Fps { get; set; } = 30;
    public string? ServiceName { get; set; }
    public string? ServerUrl { get; set; }
    public bool StreamKeyPresent { get; set; }
    public ObsLogFindings? Log { get; set; }
    public List<string> Notes { get; } = new();

    /// <summary>Normalized encoder family: NVENC / x264 / QSV / AMF / unknown.</summary>
    public string EncoderFamily
    {
        get
        {
            var id = EncoderId?.ToLowerInvariant() ?? "";
            if (id.Contains("nvenc")) return "NVENC";
            if (id.Contains("qsv")) return "QSV";
            if (id.Contains("amf") || id.Contains("amd")) return "AMF";
            if (id.Contains("x264")) return "x264";
            return id.Length == 0 ? "x264" : "unknown";
        }
    }
}

public sealed class ObsLogFindings
{
    public string? LogFile { get; set; }
    public long? DroppedCount { get; set; }
    public double? DroppedPct { get; set; }
    public double? RenderLagPct { get; set; }
    public double? EncodeLagPct { get; set; }
    public int Disconnects { get; set; }
}
