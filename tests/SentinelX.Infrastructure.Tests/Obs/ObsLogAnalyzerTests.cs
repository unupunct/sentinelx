using SentinelX.Infrastructure.Obs;

namespace SentinelX.Infrastructure.Tests.Obs;

public class ObsLogAnalyzerTests : IDisposable
{
    private readonly string _logsDir;

    public ObsLogAnalyzerTests()
    {
        _logsDir = Path.Combine(Path.GetTempPath(), $"sentinelx-obslogs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_logsDir);
    }

    [Fact]
    public void AnalyzeLatestLog_MissingDirectory_ReturnsNull()
    {
        var analyzer = new ObsLogAnalyzer();

        var result = analyzer.AnalyzeLatestLog(Path.Combine(_logsDir, "does-not-exist"));

        Assert.Null(result);
    }

    [Fact]
    public void AnalyzeLatestLog_EmptyDirectory_ReturnsNull()
    {
        var analyzer = new ObsLogAnalyzer();

        var result = analyzer.AnalyzeLatestLog(_logsDir);

        Assert.Null(result);
    }

    [Fact]
    public void AnalyzeLatestLog_ParsesDroppedFramesLagAndDisconnects()
    {
        var log = string.Join('\n', new[]
        {
            "12:00:00: Output 'adv_stream': Total frames output: 100000",
            "12:00:01: Output 'adv_stream': Number of dropped frames due to insufficient bandwidth/connection stalls: 250 (2.5%)",
            "12:00:02: Output 'adv_stream': lagged frames due to rendering lag/stalls (0.8%)",
            "12:00:03: Output 'adv_stream': skipped frames due to encoding lag/stalls (1.2%)",
            "12:00:10: Disconnected from server",
            "12:00:20: Reconnecting...",
            "12:00:25: Disconnected from server"
        });
        WriteLog("2026-07-27 12-00-00.txt", log);

        var result = new ObsLogAnalyzer().AnalyzeLatestLog(_logsDir);

        Assert.NotNull(result);
        Assert.Equal(250, result!.DroppedCount);
        Assert.Equal(2.5, result.DroppedPct);
        Assert.Equal(0.8, result.RenderLagPct);
        Assert.Equal(1.2, result.EncodeLagPct);
        Assert.Equal(2, result.Disconnects);
    }

    [Fact]
    public void AnalyzeLatestLog_PicksNewestFileByLastWriteTime()
    {
        WriteLog("older.txt", "dropped frames due to insufficient bandwidth: 10 (0.1%)");
        Thread.Sleep(50);
        WriteLog("newer.txt", "dropped frames due to insufficient bandwidth: 999 (9.9%)");

        var result = new ObsLogAnalyzer().AnalyzeLatestLog(_logsDir);

        Assert.NotNull(result);
        Assert.Equal("newer.txt", result!.LogFile);
        Assert.Equal(999, result.DroppedCount);
    }

    [Fact]
    public void AnalyzeLatestLog_NoMatchingLines_ReturnsEmptyFindings()
    {
        WriteLog("clean.txt", "12:00:00: Starting recording\n12:00:01: Stopping recording");

        var result = new ObsLogAnalyzer().AnalyzeLatestLog(_logsDir);

        Assert.NotNull(result);
        Assert.Null(result!.DroppedCount);
        Assert.Null(result.RenderLagPct);
        Assert.Null(result.EncodeLagPct);
        Assert.Equal(0, result.Disconnects);
    }

    private void WriteLog(string fileName, string content) =>
        File.WriteAllText(Path.Combine(_logsDir, fileName), content);

    public void Dispose()
    {
        if (Directory.Exists(_logsDir))
        {
            Directory.Delete(_logsDir, recursive: true);
        }
    }
}
