using System.Diagnostics;
using SentinelX.Core.Interfaces;
using SentinelX.Core.Models;
using SentinelX.Infrastructure.Obs;

namespace SentinelX.Infrastructure.Engines;

/// <summary>
/// Both a live <see cref="IMetricsSource"/> (obs-websocket skipped-frames/bitrate sparkline,
/// 2s cadence) and a deeper <see cref="IDiagnosticEngine"/> (config/log audit run on demand).
/// Wraps the ported, diagnose-only <see cref="ObsConfigService"/>/<see cref="ObsWebSocketService"/>
/// — this class only reads OBS state, it never writes to OBS's config or calls any websocket
/// operation beyond GetStreamStatus. Bitrate-vs-upload-headroom (which needs a live speed
/// measurement) is intentionally left to a future cross-engine correlation pass rather than
/// duplicated here — see <see cref="StreamValidationEngine"/> for network/speed checks.
/// </summary>
public sealed class ObsEngine : IDiagnosticEngine, IMetricsSource, IDisposable
{
    private const double FramesGreenPct = 0.5;
    private const double FramesYellowPct = 3.0;

    private readonly IHealthScoreCalculator _healthScoreCalculator;
    private readonly ObsConfigService _configService;
    private readonly ObsWebSocketService _webSocketService;

    public ObsEngine(IHealthScoreCalculator healthScoreCalculator)
    {
        _healthScoreCalculator = healthScoreCalculator;
        _configService = new ObsConfigService(new ObsLogAnalyzer());
        _webSocketService = new ObsWebSocketService();
    }

    public string Id => "obs";
    public string DisplayName => "OBS Studio";
    public EngineCategory Category => EngineCategory.Obs;
    public TimeSpan SamplingInterval => TimeSpan.FromSeconds(2);

    public async Task<MetricSample> SampleAsync(CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, double>();
        try
        {
            var status = await _webSocketService.GetStatusAsync(cancellationToken);
            values["Connected"] = status.Connected ? 1 : 0;
            values["Streaming"] = status.Streaming ? 1 : 0;
            values["SkippedFramesPercent"] = status.SkippedPct;
            values["CongestionPercent"] = status.CongestionPct;
            values["KbpsOut"] = status.KbpsOut;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // No live data available (OBS closed, WebSocket disabled, etc.) — an empty
            // sample just means the sparkline stays flat, never a crash of the sampling loop.
        }
        return new MetricSample(Id, DateTimeOffset.Now, values);
    }

    public async Task<EngineResult> RunAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var collectedAt = DateTimeOffset.Now;
        var findings = new List<Finding>();
        string? error = null;

        try
        {
            var audit = await Task.Run(() => _configService.ReadAudit(), cancellationToken);
            findings.AddRange(BuildFindings(audit));
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        stopwatch.Stop();
        var score = error is null ? _healthScoreCalculator.FromFindings(findings) : 0;
        return new EngineResult(EngineCategory.Obs, score, findings, collectedAt, stopwatch.Elapsed, error);
    }

    private static IEnumerable<Finding> BuildFindings(ObsAudit audit)
    {
        if (audit.State == ObsState.NotInstalled)
        {
            yield return new Finding(
                "OBS Studio is not installed",
                "No OBS Studio installation was found on this PC (checked the default Program " +
                "Files locations and the registry).",
                Severity.Info,
                0.9,
                "Install OBS Studio from obsproject.com if streaming/recording is needed on this PC.",
                "None — informational only.");
            yield break;
        }

        if (audit.State == ObsState.InstalledNotConfigured)
        {
            yield return new Finding(
                "OBS Studio installed but never configured",
                "OBS is installed but has never been launched on this PC, so there is no " +
                "profile/config to audit yet.",
                Severity.Info,
                0.9,
                "Open OBS once and set up a profile, then run this scan again.",
                "None — informational only.");
            yield break;
        }

        var outCy = audit.OutCY > 0 ? audit.OutCY : audit.BaseCY;
        if (outCy > 0)
        {
            var minKbps = MinBitrateFor(outCy, audit.Fps);
            if (audit.VBitrateKbps < minKbps * 0.75)
            {
                yield return new Finding(
                    "Video bitrate is too low for the output resolution/FPS",
                    $"OBS is set to {audit.OutCX}x{outCy} @ {audit.Fps:0.##} fps at only " +
                    $"{audit.VBitrateKbps} kbps (recommended at least {minKbps} kbps). The picture " +
                    "will look blocky and blurry, especially in motion.",
                    Severity.Critical,
                    0.85,
                    $"In OBS: Settings > Output, raise Video Bitrate to at least {minKbps} kbps, " +
                    "or lower the output resolution/FPS in Settings > Video.",
                    "High — noticeably degrades stream/recording picture quality.");
            }
            else if (audit.VBitrateKbps < minKbps)
            {
                yield return new Finding(
                    "Video bitrate is a little low for the output resolution/FPS",
                    $"OBS is set to {audit.OutCX}x{outCy} @ {audit.Fps:0.##} fps at " +
                    $"{audit.VBitrateKbps} kbps (recommended at least {minKbps} kbps).",
                    Severity.Warning,
                    0.7,
                    $"In OBS: Settings > Output, consider raising Video Bitrate toward {minKbps} kbps " +
                    "if upload bandwidth allows it.",
                    "Medium — some quality loss during high-motion scenes.");
            }

            if (audit.Fps > 30 && audit.VBitrateKbps < 4500)
            {
                yield return new Finding(
                    "High frame rate with low bitrate",
                    $"OBS is set to {audit.Fps:0} fps at {audit.VBitrateKbps} kbps. 60 fps needs " +
                    "considerably more bitrate to look good than 30 fps does.",
                    Severity.Warning,
                    0.6,
                    "In OBS: Settings > Video, consider dropping Common FPS Values to 30 unless " +
                    "the bitrate can be raised accordingly.",
                    "Medium — picture quality suffers more visibly at 60fps under this bitrate.");
            }
        }

        if (audit.BaseCX > 0 && audit.BaseCY > 0 && audit.OutCX > 0 && audit.OutCY > 0)
        {
            var baseAspect = (double)audit.BaseCX / audit.BaseCY;
            var outAspect = (double)audit.OutCX / audit.OutCY;
            if (Math.Abs(baseAspect - outAspect) > 0.01)
            {
                yield return new Finding(
                    "Canvas and output resolution have different aspect ratios",
                    $"Canvas is {audit.BaseCX}x{audit.BaseCY} but output is {audit.OutCX}x{audit.OutCY} " +
                    "— the picture will get stretched or cropped.",
                    Severity.Warning,
                    0.85,
                    "In OBS: Settings > Video, make Base (Canvas) and Output (Scaled) resolutions " +
                    "the same aspect ratio (e.g. both 16:9).",
                    "Medium — visibly distorts the stream/recording picture.");
            }
        }

        if (!audit.KeyintIsAuto && audit.KeyintSec != 2)
        {
            yield return new Finding(
                "Keyframe interval isn't the platform-recommended 2 seconds",
                $"Keyframe interval is set to {audit.KeyintSec}s. Most streaming platforms " +
                "require exactly 2 seconds for reliable playback and transcoding.",
                audit.KeyintSec == 1 ? Severity.Warning : Severity.Critical,
                0.8,
                "In OBS: Settings > Output (Advanced mode), set Keyframe Interval to 2 seconds.",
                "Medium to high — platforms may reject the stream or transcode it poorly.");
        }

        if (audit.Log is { } log)
        {
            if (log.DroppedPct is { } droppedPct && droppedPct > FramesGreenPct)
            {
                yield return new Finding(
                    "Dropped frames in the last OBS session (network-side)",
                    $"The last session ({log.LogFile}) dropped {droppedPct:0.#}% of frames " +
                    $"({log.DroppedCount} frames) due to the network not keeping up with the " +
                    "configured bitrate.",
                    droppedPct > FramesYellowPct ? Severity.Critical : Severity.Warning,
                    0.75,
                    "Lower the video bitrate, switch to a wired connection, and avoid other " +
                    "uploads while streaming.",
                    "High — directly visible as stutter/freezing for viewers.");
            }

            var lagPct = Math.Max(log.RenderLagPct ?? 0, log.EncodeLagPct ?? 0);
            if (log.RenderLagPct is not null || log.EncodeLagPct is not null)
            {
                if (lagPct > FramesGreenPct)
                {
                    yield return new Finding(
                        "PC couldn't keep up in the last OBS session (rendering/encoding-side)",
                        $"The last session showed {log.RenderLagPct ?? 0:0.#}% render lag and " +
                        $"{log.EncodeLagPct ?? 0:0.#}% encoding lag — this is a local performance " +
                        "bottleneck, not a network problem.",
                        lagPct > FramesYellowPct ? Severity.Critical : Severity.Warning,
                        0.75,
                        "Close unused programs, lower the output resolution, or switch to a " +
                        "GPU encoder (NVENC/QSV/AMF) if currently using x264.",
                        "High — directly visible as stutter/freezing for viewers.");
                }
            }

            if (log.Disconnects > 0)
            {
                yield return new Finding(
                    $"{log.Disconnects} disconnect(s) in the last OBS session",
                    "The stream disconnected during the last session, which usually points to " +
                    "unstable internet.",
                    log.Disconnects >= 2 ? Severity.Critical : Severity.Warning,
                    0.7,
                    "Check jitter/packet loss with the Streaming Validation module and prefer a " +
                    "wired connection.",
                    "High — viewers see the stream cut out entirely.");
            }
        }

        foreach (var note in audit.Notes)
        {
            yield return new Finding(
                "OBS configuration note",
                note,
                Severity.Info,
                0.6,
                "No action needed unless this affects a setting you rely on.",
                "None — informational only.");
        }
    }

    private static int MinBitrateFor(int outputHeight, double fps) => outputHeight switch
    {
        >= 1080 => fps > 30 ? 6000 : 4500,
        >= 720 => fps > 30 ? 4500 : 3000,
        >= 540 => 2000,
        _ => 1200
    };

    public void Dispose() => _webSocketService.Dispose();
}
