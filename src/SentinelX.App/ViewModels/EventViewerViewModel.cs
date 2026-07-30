using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Infrastructure.EventLog;

namespace SentinelX.App.ViewModels;

public sealed record LookbackOption(string Label, TimeSpan Duration);
public sealed record LevelFilterOption(string Label, EventEntryLevel? Level);

/// <summary>
/// Drives the Event Viewer tab: browses recent Warning/Error/Critical entries from the System
/// and Application Windows Event Logs via <see cref="EventLogService"/>. This is diagnose-only
/// — it surfaces and (for a small curated set of common event IDs) explains entries, but never
/// modifies services, drivers, or settings; any fix is left for the user to apply themselves.
/// </summary>
public sealed partial class EventViewerViewModel : ObservableObject
{
    private readonly EventLogService _eventLogService;
    private List<EventEntry> _allEntries = new();

    public IReadOnlyList<LookbackOption> LookbackOptions { get; } = new[]
    {
        new LookbackOption("Last hour", TimeSpan.FromHours(1)),
        new LookbackOption("Last 24 hours", TimeSpan.FromHours(24)),
        new LookbackOption("Last 7 days", TimeSpan.FromDays(7))
    };

    public IReadOnlyList<LevelFilterOption> LevelFilterOptions { get; } = new[]
    {
        new LevelFilterOption("All levels", null),
        new LevelFilterOption("Critical", EventEntryLevel.Critical),
        new LevelFilterOption("Error", EventEntryLevel.Error),
        new LevelFilterOption("Warning", EventEntryLevel.Warning)
    };

    [ObservableProperty]
    private LookbackOption _selectedLookback;

    [ObservableProperty]
    private LevelFilterOption _selectedLevelFilter;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "Not loaded yet";

    [ObservableProperty]
    private int _criticalCount;

    [ObservableProperty]
    private int _errorCount;

    [ObservableProperty]
    private int _warningCount;

    public ObservableCollection<EventEntry> Entries { get; } = new();

    public EventViewerViewModel(EventLogService eventLogService)
    {
        _eventLogService = eventLogService;
        _selectedLookback = LookbackOptions[1]; // Last 24 hours
        _selectedLevelFilter = LevelFilterOptions[0]; // All levels

        _ = RefreshAsync();
    }

    partial void OnSelectedLookbackChanged(LookbackOption value) => _ = RefreshAsync();

    partial void OnSelectedLevelFilterChanged(LevelFilterOption value) => ApplyFilter();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        StatusText = "Loading...";
        try
        {
            _allEntries = await Task.Run(() => _eventLogService.GetRecentEntries(SelectedLookback.Duration).ToList());
            CriticalCount = _allEntries.Count(e => e.Level == EventEntryLevel.Critical);
            ErrorCount = _allEntries.Count(e => e.Level == EventEntryLevel.Error);
            WarningCount = _allEntries.Count(e => e.Level == EventEntryLevel.Warning);
            ApplyFilter();
            StatusText = $"{_allEntries.Count} entries · {SelectedLookback.Label.ToLowerInvariant()} · refreshed {DateTime.Now:t}";
        }
        catch (Exception ex)
        {
            StatusText = "Failed to read event logs: " + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        Entries.Clear();
        var filtered = SelectedLevelFilter.Level is { } level
            ? _allEntries.Where(e => e.Level == level)
            : _allEntries;
        foreach (var entry in filtered)
        {
            Entries.Add(entry);
        }
    }
}
