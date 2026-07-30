using System.Globalization;
using System.Text.RegularExpressions;

namespace SentinelX.Infrastructure.Obs;

/// <summary>Scans the most recent OBS log for dropped frames, lag, and disconnects.</summary>
public sealed partial class ObsLogAnalyzer
{
    [GeneratedRegex(@"dropped frames.*?:\s*(\d+)\s*\(([\d.]+)%\)", RegexOptions.IgnoreCase)]
    private static partial Regex DroppedRegex();

    [GeneratedRegex(@"lagged frames due to rendering.*?\(([\d.]+)%\)", RegexOptions.IgnoreCase)]
    private static partial Regex RenderLagRegex();

    [GeneratedRegex(@"skipped frames due to encoding.*?\(([\d.]+)%\)", RegexOptions.IgnoreCase)]
    private static partial Regex EncodeLagRegex();

    public ObsLogFindings? AnalyzeLatestLog(string logsDir)
    {
        if (!Directory.Exists(logsDir)) return null;
        var latest = new DirectoryInfo(logsDir).GetFiles("*.txt")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
        if (latest is null) return null;

        var findings = new ObsLogFindings { LogFile = latest.Name };
        string[] lines;
        try
        {
            // OBS may hold the log open — share-tolerant read
            using var fs = new FileStream(latest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            lines = sr.ReadToEnd().Split('\n');
        }
        catch
        {
            return findings;
        }

        foreach (var line in lines)
        {
            var dropped = DroppedRegex().Match(line);
            if (dropped.Success)
            {
                if (long.TryParse(dropped.Groups[1].Value, out var cnt)) findings.DroppedCount = cnt;
                if (double.TryParse(dropped.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                    findings.DroppedPct = pct;
                continue;
            }
            var render = RenderLagRegex().Match(line);
            if (render.Success)
            {
                if (double.TryParse(render.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                    findings.RenderLagPct = pct;
                continue;
            }
            var encode = EncodeLagRegex().Match(line);
            if (encode.Success)
            {
                if (double.TryParse(encode.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                    findings.EncodeLagPct = pct;
                continue;
            }
            if (line.Contains("Disconnected from", StringComparison.OrdinalIgnoreCase))
                findings.Disconnects++;
        }
        return findings;
    }
}
