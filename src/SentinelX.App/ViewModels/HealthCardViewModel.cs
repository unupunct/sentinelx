using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SentinelX.Core.Models;

namespace SentinelX.App.ViewModels;

/// <summary>
/// One dashboard health card. <see cref="LiveSamples"/> stays empty for engines that don't
/// implement <see cref="Core.Interfaces.IMetricsSource"/> — the card's sparkline simply
/// doesn't render (see the CountToVisibilityConverter binding in HealthCardControl.xaml).
/// </summary>
public sealed partial class HealthCardViewModel : ObservableObject
{
    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private EngineCategory _category;

    [ObservableProperty]
    private int _score = 100;

    public ObservableCollection<Finding> Findings { get; } = new();

    public ObservableCollection<double> LiveSamples { get; } = new();
}
