using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SentinelX.Core.Interfaces;
using SentinelX.Core.Models;
using SentinelX.Infrastructure.Monitoring;

namespace SentinelX.App.ViewModels;

/// <summary>
/// Drives the main dashboard: one <see cref="HealthCardViewModel"/> per registered engine,
/// the overall stability gauge, the historical trend sparkline, and the live OBS
/// skipped-frames sparkline fed by <see cref="LiveMonitoringService"/> via the messenger.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject, IRecipient<MetricSampleMessage>
{
    private const int MaxLiveSamples = 300;
    private const int MaxTrendPoints = 30;

    private readonly IEnumerable<IDiagnosticEngine> _engines;
    private readonly IScoringService _scoringService;
    private readonly IHistoryRepository _historyRepository;
    private readonly HealthCardViewModel? _obsCard;

    [ObservableProperty]
    private int _overallScore;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _lastScanText = "Never scanned yet";

    public ObservableCollection<HealthCardViewModel> HealthCards { get; } = new();

    public ObservableCollection<double> HistoryTrend { get; } = new();

    public DashboardViewModel(
        IEnumerable<IDiagnosticEngine> engines,
        IScoringService scoringService,
        IHistoryRepository historyRepository)
    {
        _engines = engines;
        _scoringService = scoringService;
        _historyRepository = historyRepository;

        foreach (var engine in _engines)
        {
            var card = new HealthCardViewModel
            {
                DisplayName = engine.DisplayName,
                Category = engine.Category
            };
            HealthCards.Add(card);

            if (engine.Category == EngineCategory.Obs)
            {
                _obsCard = card;
            }
        }

        WeakReferenceMessenger.Default.Register(this);
        _ = LoadHistoryAsync();
    }

    public void Receive(MetricSampleMessage message)
    {
        if (_obsCard is null || message.Sample.SourceId != "obs")
        {
            return;
        }

        if (!message.Sample.Values.TryGetValue("SkippedFramesPercent", out var skippedPct))
        {
            return;
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            _obsCard.LiveSamples.Add(skippedPct);
            while (_obsCard.LiveSamples.Count > MaxLiveSamples)
            {
                _obsCard.LiveSamples.RemoveAt(0);
            }
        });
    }

    [RelayCommand]
    private async Task RunScanAsync()
    {
        IsScanning = true;
        try
        {
            var results = await Task.WhenAll(_engines.Select(engine => engine.RunAsync(CancellationToken.None)));
            var categoryScores = results.ToDictionary(r => r.Category, r => r.HealthScore);
            OverallScore = _scoringService.ComputeOverallScore(categoryScores);

            foreach (var result in results)
            {
                var card = HealthCards.FirstOrDefault(c => c.Category == result.Category);
                if (card is null)
                {
                    continue;
                }

                card.Score = result.HealthScore;
                card.Findings.Clear();
                foreach (var finding in result.Findings)
                {
                    card.Findings.Add(finding);
                }
            }

            var allFindings = results.SelectMany(r => r.Findings).ToList();
            var snapshot = new ScanSnapshot(DateTimeOffset.Now, OverallScore, categoryScores, allFindings);
            await _historyRepository.SaveScanAsync(snapshot, CancellationToken.None);

            AppendTrendPoint(OverallScore);
            LastScanText = $"Last scan: {DateTimeOffset.Now:t}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            var recent = await _historyRepository.GetRecentAsync(MaxTrendPoints, CancellationToken.None);
            if (recent.Count == 0)
            {
                return;
            }

            foreach (var snapshot in recent.Reverse())
            {
                HistoryTrend.Add(snapshot.OverallScore);
            }

            OverallScore = recent[0].OverallScore;
            LastScanText = $"Last scan: {recent[0].Timestamp:t}";
        }
        catch
        {
            // History is a convenience, not a requirement — a fresh/corrupt DB just starts empty.
        }
    }

    private void AppendTrendPoint(int score)
    {
        HistoryTrend.Add(score);
        while (HistoryTrend.Count > MaxTrendPoints)
        {
            HistoryTrend.RemoveAt(0);
        }
    }
}
