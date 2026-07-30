using System.Diagnostics;
using System.Management;

namespace SentinelX.Infrastructure.Monitoring;

public sealed class SystemMetricsSnapshot
{
    public double CpuUsagePercent { get; init; }
    public double MemoryUsagePercent { get; init; }
    public double MemoryUsedGb { get; init; }
    public double MemoryTotalGb { get; init; }
    public double? GpuUsagePercent { get; init; }
    public string? GpuUnavailableReason { get; init; }
}

/// <summary>
/// Cheap live CPU/RAM/GPU sampling for the Live Monitoring tab. CPU and RAM use standard
/// perf-counter/WMI sources; GPU has no vendor-neutral BCL-only API, so it's a best-effort
/// read of the "GPU Engine" performance counter category (the same source Task Manager's GPU
/// graphs use) — sums "Utilization Percentage" across all "engtype_3D" instances. This can
/// read 0% or be unavailable on some driver/hardware combinations; <see cref="SystemMetricsSnapshot.GpuUnavailableReason"/>
/// explains why when that happens rather than silently showing a wrong number.
/// </summary>
public sealed class SystemMetricsService : IDisposable
{
    private readonly PerformanceCounter _cpuCounter = new("Processor", "% Processor Time", "_Total");
    private readonly List<PerformanceCounter> _gpuEngineCounters = new();
    private bool _gpuCountersInitialized;
    private string? _gpuUnavailableReason;

    public SystemMetricsSnapshot Sample()
    {
        double cpu = 0;
        try { cpu = _cpuCounter.NextValue(); } catch { /* first read after construction is unreliable; self-corrects next tick */ }

        var (usedGb, totalGb, usedPct) = SampleMemory();
        var gpu = SampleGpu();

        return new SystemMetricsSnapshot
        {
            CpuUsagePercent = cpu,
            MemoryUsedGb = usedGb,
            MemoryTotalGb = totalGb,
            MemoryUsagePercent = usedPct,
            GpuUsagePercent = gpu,
            GpuUnavailableReason = gpu is null ? _gpuUnavailableReason : null
        };
    }

    private static (double UsedGb, double TotalGb, double UsedPct) SampleMemory()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (ManagementBaseObject result in searcher.Get())
            {
                using (result)
                {
                    var totalKb = Convert.ToDouble(result["TotalVisibleMemorySize"]);
                    var freeKb = Convert.ToDouble(result["FreePhysicalMemory"]);
                    if (totalKb <= 0) return (0, 0, 0);

                    var totalGb = totalKb / 1024.0 / 1024.0;
                    var usedGb = (totalKb - freeKb) / 1024.0 / 1024.0;
                    var usedPct = (totalKb - freeKb) / totalKb * 100.0;
                    return (usedGb, totalGb, usedPct);
                }
            }
        }
        catch
        {
            // WMI unavailable in this environment — reported as zero, not a failure.
        }
        return (0, 0, 0);
    }

    private double? SampleGpu()
    {
        if (!_gpuCountersInitialized)
        {
            InitializeGpuCounters();
        }
        if (_gpuEngineCounters.Count == 0)
        {
            return null;
        }

        double total = 0;
        foreach (var counter in _gpuEngineCounters)
        {
            try { total += counter.NextValue(); } catch { /* instance may have disappeared — skip it */ }
        }
        return Math.Min(100, total);
    }

    private void InitializeGpuCounters()
    {
        _gpuCountersInitialized = true;
        try
        {
            if (!PerformanceCounterCategory.Exists("GPU Engine"))
            {
                _gpuUnavailableReason = "The \"GPU Engine\" performance counter category is not present on this machine.";
                return;
            }

            var category = new PerformanceCounterCategory("GPU Engine");
            var instanceNames = category.GetInstanceNames()
                .Where(n => n.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var name in instanceNames)
            {
                try
                {
                    _gpuEngineCounters.Add(new PerformanceCounter("GPU Engine", "Utilization Percentage", name, true));
                }
                catch
                {
                    // Instance can disappear between enumeration and construction (process exited) — skip it.
                }
            }

            if (_gpuEngineCounters.Count == 0)
            {
                _gpuUnavailableReason = "No 3D GPU engine instances were found to sample.";
            }
        }
        catch
        {
            _gpuUnavailableReason = "GPU Engine performance counters are unavailable on this machine.";
        }
    }

    public void Dispose()
    {
        _cpuCounter.Dispose();
        foreach (var counter in _gpuEngineCounters)
        {
            counter.Dispose();
        }
    }
}
