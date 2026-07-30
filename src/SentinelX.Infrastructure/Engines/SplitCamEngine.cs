using System.Diagnostics;
using System.Management;
using SentinelX.Core.Interfaces;
using SentinelX.Core.Models;
using SentinelX.Infrastructure.Monitoring;

namespace SentinelX.Infrastructure.Engines;

/// <summary>
/// One-shot SplitCam presence/health check. Deliberately limited in scope: no confirmed,
/// documented SplitCam config file format was found during research, so this engine only
/// reports what can be verified without guessing at an undocumented format — install
/// detection, process-running state (reusing <see cref="LiveMonitorService"/>'s process-watch
/// pattern), and virtual-camera device presence via WMI. Deeper features (current
/// resolution/FPS, driver-instability detection, camera-conflict detection) need on-machine
/// verification against a real SplitCam install before they can be added reliably — see the
/// Sentinel X phase 1 plan.
/// </summary>
public sealed class SplitCamEngine : IDiagnosticEngine
{
    private static readonly string[] InstallPaths =
    {
        @"C:\Program Files (x86)\SplitCam",
        @"C:\Program Files\SplitCam"
    };

    private readonly IHealthScoreCalculator _healthScoreCalculator;
    private readonly LiveMonitorService _liveMonitor;

    public SplitCamEngine(IHealthScoreCalculator healthScoreCalculator, LiveMonitorService liveMonitor)
    {
        _healthScoreCalculator = healthScoreCalculator;
        _liveMonitor = liveMonitor;
    }

    public string Id => "splitcam";
    public string DisplayName => "SplitCam";
    public EngineCategory Category => EngineCategory.SplitCam;

    public async Task<EngineResult> RunAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var collectedAt = DateTimeOffset.Now;
        var findings = new List<Finding>();
        string? error = null;

        try
        {
            var installPath = InstallPaths.FirstOrDefault(Directory.Exists);
            var running = _liveMonitor.GetProcessSnapshot(new[] { "SplitCam" }).FirstOrDefault();
            var hasVirtualCam = await Task.Run(HasVirtualCameraDevice, cancellationToken);

            if (installPath is null)
            {
                findings.Add(new Finding(
                    "SplitCam is not installed",
                    "No SplitCam installation was found at the default Program Files locations.",
                    Severity.Info,
                    0.6,
                    "Install SplitCam from splitcam.com if virtual-camera compositing is needed.",
                    "None — informational only."));
            }
            else if (running is null || !running.IsRunning)
            {
                findings.Add(new Finding(
                    "SplitCam is installed but not running",
                    $"SplitCam was found at {installPath} but no SplitCam process is currently running.",
                    Severity.Info,
                    0.7,
                    "Launch SplitCam if a virtual camera feed is expected for this session.",
                    "None — informational only."));
            }
            else
            {
                findings.Add(new Finding(
                    "SplitCam is running",
                    running.StatusText,
                    Severity.Info,
                    0.8,
                    "No action needed.",
                    "None — informational only."));

                if (running.CpuPct > 40)
                {
                    findings.Add(new Finding(
                        "SplitCam is using significant CPU",
                        $"SplitCam is currently using {running.CpuPct:0.0}% CPU, which can compete " +
                        "with OBS's own encoding/rendering load.",
                        Severity.Warning,
                        0.5,
                        "Reduce SplitCam's active effects/sources, or move OBS to a GPU encoder " +
                        "to free up CPU headroom.",
                        "Medium — can contribute to render/encode lag reported by the OBS module."));
                }
            }

            if (installPath is not null && !hasVirtualCam)
            {
                findings.Add(new Finding(
                    "SplitCam virtual camera device not detected",
                    "A best-effort WMI scan for camera-class devices did not find a SplitCam " +
                    "virtual camera. This check is limited — SplitCam's exact device naming " +
                    "wasn't verified against a real install, so this may be a false positive.",
                    Severity.Warning,
                    0.4,
                    "If OBS/Zoom/Discord can't see a SplitCam camera source, try reinstalling " +
                    "SplitCam or restarting the PC so its virtual camera driver re-registers.",
                    "Medium — apps that expect a SplitCam camera source may not see one."));
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        stopwatch.Stop();
        var score = error is null ? _healthScoreCalculator.FromFindings(findings) : 0;
        return new EngineResult(EngineCategory.SplitCam, score, findings, collectedAt, stopwatch.Elapsed, error);
    }

    private static bool HasVirtualCameraDevice()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_PnPEntity WHERE PNPClass='Camera' OR PNPClass='Image'");
            foreach (ManagementBaseObject device in searcher.Get())
            {
                using (device)
                {
                    var name = device["Name"] as string ?? "";
                    if (name.Contains("SplitCam", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch
        {
            // WMI unavailable in this environment — treated as "not detected", not a failure.
        }
        return false;
    }
}
