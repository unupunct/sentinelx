using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using SentinelX.Infrastructure.Monitoring;

namespace SentinelX.App.ViewModels;

/// <summary>
/// Drives the Live Monitoring tab: network adapter/throughput and watched-process stats
/// polled directly from <see cref="LiveMonitorService"/> on a 1s UI-thread timer (the same
/// cadence/pattern its source, BestStudioOBSChecker, used — these calls are cheap enough to
/// call straight from a timer, no background thread needed), plus OBS's live stream stats
/// received over the same <see cref="MetricSampleMessage"/> bus the Dashboard tab's sparkline
/// already subscribes to.
/// </summary>
public sealed partial class LiveMonitoringViewModel : ObservableObject, IRecipient<MetricSampleMessage>
{
    private const int MaxSamples = 120; // ~2 minutes of history at 1 sample/sec

    // Sentinel X's own process scope for phase 1 — OBS and SplitCam, not a
    // studio-specific watchlist.
    private static readonly string[] Watchlist = { "obs64", "obs32", "SplitCam" };

    private readonly LiveMonitorService _liveMonitor;
    private readonly SystemMetricsService _systemMetrics;
    private readonly DispatcherTimer _timer;

    [ObservableProperty]
    private double _cpuUsagePercent;

    [ObservableProperty]
    private double _memoryUsagePercent;

    [ObservableProperty]
    private double _memoryUsedGb;

    [ObservableProperty]
    private double _memoryTotalGb;

    [ObservableProperty]
    private double? _gpuUsagePercent;

    [ObservableProperty]
    private string? _gpuUnavailableReason;

    [ObservableProperty]
    private string _adapterDescription = "No active adapter detected";

    [ObservableProperty]
    private string _connectionSummary = "";

    [ObservableProperty]
    private double _uploadMbps;

    [ObservableProperty]
    private double _downloadMbps;

    [ObservableProperty]
    private string _obsStatusText = "No live data yet";

    [ObservableProperty]
    private bool _obsStreaming;

    [ObservableProperty]
    private double _obsSkippedFramesPercent;

    [ObservableProperty]
    private double _obsCongestionPercent;

    [ObservableProperty]
    private double _obsKbpsOut;

    public ObservableCollection<double> UploadSamples { get; } = new();
    public ObservableCollection<double> DownloadSamples { get; } = new();
    public ObservableCollection<double> CpuSamples { get; } = new();
    public ObservableCollection<double> MemorySamples { get; } = new();
    public ObservableCollection<double> GpuSamples { get; } = new();
    public ObservableCollection<ProcRow> Processes { get; } = new();

    public LiveMonitoringViewModel(LiveMonitorService liveMonitor, SystemMetricsService systemMetrics)
    {
        _liveMonitor = liveMonitor;
        _systemMetrics = systemMetrics;

        WeakReferenceMessenger.Default.Register(this);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
        Poll();
    }

    private void Poll()
    {
        var metrics = _systemMetrics.Sample();
        CpuUsagePercent = metrics.CpuUsagePercent;
        MemoryUsagePercent = metrics.MemoryUsagePercent;
        MemoryUsedGb = metrics.MemoryUsedGb;
        MemoryTotalGb = metrics.MemoryTotalGb;
        GpuUsagePercent = metrics.GpuUsagePercent;
        GpuUnavailableReason = metrics.GpuUnavailableReason;
        AppendSample(CpuSamples, metrics.CpuUsagePercent);
        AppendSample(MemorySamples, metrics.MemoryUsagePercent);
        AppendSample(GpuSamples, metrics.GpuUsagePercent ?? 0);

        var adapter = _liveMonitor.GetAdapterInfo();
        if (adapter is not null)
        {
            AdapterDescription = adapter.Description;
            ConnectionSummary = adapter.LinkSpeedMbps > 0
                ? $"{(adapter.IsWifi ? "WiFi" : "Ethernet")} · {adapter.LinkSpeedMbps} Mbps link"
                : adapter.IsWifi ? "WiFi" : "Ethernet";
        }
        else
        {
            AdapterDescription = "No active adapter detected";
            ConnectionSummary = "";
        }

        if (_liveMonitor.SampleThroughput() is { } throughput)
        {
            UploadMbps = throughput.UpMbps;
            DownloadMbps = throughput.DownMbps;
            AppendSample(UploadSamples, throughput.UpMbps);
            AppendSample(DownloadSamples, throughput.DownMbps);
        }

        Processes.Clear();
        foreach (var row in _liveMonitor.GetProcessSnapshot(Watchlist))
        {
            Processes.Add(row);
        }
    }

    private static void AppendSample(ObservableCollection<double> samples, double value)
    {
        samples.Add(value);
        while (samples.Count > MaxSamples)
        {
            samples.RemoveAt(0);
        }
    }

    public void Receive(MetricSampleMessage message)
    {
        if (message.Sample.SourceId != "obs")
        {
            return;
        }

        var values = message.Sample.Values;
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (values.Count == 0)
            {
                // ObsEngine reports an empty sample when OBS isn't running or the
                // WebSocket server is off — reset rather than show stale numbers.
                ObsStatusText = "No live OBS data (not running, or WebSocket server disabled)";
                ObsStreaming = false;
                ObsSkippedFramesPercent = 0;
                ObsCongestionPercent = 0;
                ObsKbpsOut = 0;
                return;
            }

            var connected = values.GetValueOrDefault("Connected") > 0;
            ObsStreaming = values.GetValueOrDefault("Streaming") > 0;
            ObsSkippedFramesPercent = values.GetValueOrDefault("SkippedFramesPercent");
            ObsCongestionPercent = values.GetValueOrDefault("CongestionPercent");
            ObsKbpsOut = values.GetValueOrDefault("KbpsOut");
            ObsStatusText = ObsStreaming ? "Streaming"
                : connected ? "Connected, not streaming"
                : "OBS not running or WebSocket disabled";
        });
    }
}
