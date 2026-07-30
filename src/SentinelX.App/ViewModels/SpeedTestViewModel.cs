using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core.Models;
using SentinelX.Infrastructure.Networking;

namespace SentinelX.App.ViewModels;

/// <summary>
/// Drives the Speed Test tab: a full manual download/upload/bufferbloat measurement via the
/// same <see cref="SpeedTestService"/> the Streaming Validation engine uses for its short
/// on-demand check, but run at the original tuning (4 streams / 10s) since the user is
/// explicitly asking for a real measurement here rather than a quick scan — expect ~25-30s
/// total (idle ping baseline + warmup + download + upload with concurrent ping probing).
/// </summary>
public sealed partial class SpeedTestViewModel : ObservableObject
{
    private const double UploadGreenMbps = 10, UploadYellowMbps = 5;
    private const double BufferbloatGreenMs = 50, BufferbloatYellowMs = 150;
    private const int MaxLiveSamples = 200;

    private readonly SpeedTestService _speedTestService = new();
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = "Ready to test.";

    [ObservableProperty]
    private string _currentPhase = "";

    [ObservableProperty]
    private double _currentMbps;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private double? _downloadMbps;

    [ObservableProperty]
    private double? _downloadMinMbps;

    [ObservableProperty]
    private double? _uploadMbps;

    [ObservableProperty]
    private double? _uploadMinMbps;

    [ObservableProperty]
    private double? _idlePingMs;

    [ObservableProperty]
    private double? _loadedPingMs;

    [ObservableProperty]
    private double? _bufferbloatMs;

    [ObservableProperty]
    private Severity _uploadSeverity = Severity.Info;

    [ObservableProperty]
    private Severity _bufferbloatSeverity = Severity.Info;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private string? _suggestedBitrateText;

    public ObservableCollection<double> LiveSamples { get; } = new();

    public bool CanStart => !IsRunning;
    public bool CanCancel => IsRunning;

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanCancel));
        StartTestCommand.NotifyCanExecuteChanged();
        CancelTestCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartTestAsync()
    {
        IsRunning = true;
        HasResult = false;
        ErrorText = null;
        SuggestedBitrateText = null;
        LiveSamples.Clear();
        StatusText = "Measuring idle latency…";
        CurrentPhase = "";
        CurrentMbps = 0;

        _cts = new CancellationTokenSource();
        var progress = new Progress<SpeedProgress>(OnProgress);

        try
        {
            var result = await _speedTestService.RunAsync(streams: 4, seconds: 10, progress, _cts.Token);
            ApplyResult(result);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Test cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = "Test failed: " + ex.Message;
        }
        finally
        {
            IsRunning = false;
            CurrentPhase = "";
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelTest() => _cts?.Cancel();

    private void OnProgress(SpeedProgress progress)
    {
        CurrentPhase = progress.Phase;
        CurrentMbps = progress.CurrentMbps;
        StatusText = $"{progress.Phase}: {progress.CurrentMbps:0.0} Mbps ({progress.ElapsedSec:0}s / {progress.TotalSec:0}s)";

        LiveSamples.Add(progress.CurrentMbps);
        while (LiveSamples.Count > MaxLiveSamples)
        {
            LiveSamples.RemoveAt(0);
        }
    }

    private void ApplyResult(SpeedTestResult result)
    {
        DownloadMbps = result.DownloadMbps;
        DownloadMinMbps = result.DownloadMinSustainedMbps;
        UploadMbps = result.UploadMbps;
        UploadMinMbps = result.UploadMinSustainedMbps;
        IdlePingMs = result.IdlePingMs;
        LoadedPingMs = result.LoadedPingMs;
        BufferbloatMs = result.BufferbloatMs;
        ErrorText = result.Error;
        HasResult = true;

        var uploadBasis = result.UploadMinSustainedMbps ?? result.UploadMbps;
        if (uploadBasis is { } basis)
        {
            UploadSeverity = basis < UploadYellowMbps ? Severity.Critical
                : basis < UploadGreenMbps ? Severity.Warning
                : Severity.Info;

            var (kbps, combo) = SuggestBitrate(basis);
            SuggestedBitrateText = $"Recommended for this connection: {combo} (~{kbps} kbps video bitrate)";
        }

        if (result.BufferbloatMs is { } bloat)
        {
            BufferbloatSeverity = bloat > BufferbloatYellowMs ? Severity.Critical
                : bloat > BufferbloatGreenMs ? Severity.Warning
                : Severity.Info;
        }

        StatusText = result.Error is null
            ? $"Test complete at {DateTime.Now:t}"
            : "Test completed with errors — see details below.";
    }

    /// <summary>
    /// Suggests a safe streaming bitrate/settings combo for a measured upload speed (Mbps).
    /// Ported verbatim from BestStudioOBSChecker's RecommendationEngine.SuggestBitrate — budgets
    /// 70% of the measured upload so the stream stays stable under real-world variance.
    /// </summary>
    private static (int Kbps, string Combo) SuggestBitrate(double uploadMbps)
    {
        var budgetKbps = uploadMbps * 0.7 * 1000.0;
        if (budgetKbps >= 6160) return (6000, "1080p @ 60 fps, 6000 kbps video + 160 kbps audio");
        if (budgetKbps >= 4660) return (4500, "1080p @ 30 fps (or 720p60), 4500 kbps video + 160 kbps audio");
        if (budgetKbps >= 3160) return (3000, "720p @ 30 fps, 3000 kbps video + 160 kbps audio");
        if (budgetKbps >= 2160) return (2000, "540p @ 30 fps, 2000 kbps video + 160 kbps audio");
        var kbps = (int)Math.Max(500, budgetKbps - 160);
        return (kbps, $"480p @ 30 fps, {kbps} kbps — upload is very limited, a faster plan is strongly recommended");
    }
}
