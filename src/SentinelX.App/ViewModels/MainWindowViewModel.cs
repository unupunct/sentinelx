using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SentinelX.App.ViewModels;

public enum AppTab
{
    Dashboard,
    LiveMonitoring,
    EventViewer,
    SpeedTest
}

public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isDarkTheme = true;

    [ObservableProperty]
    private AppTab _activeTab = AppTab.Dashboard;

    public bool IsDashboardSelected => ActiveTab == AppTab.Dashboard;
    public bool IsLiveMonitoringSelected => ActiveTab == AppTab.LiveMonitoring;
    public bool IsEventViewerSelected => ActiveTab == AppTab.EventViewer;
    public bool IsSpeedTestSelected => ActiveTab == AppTab.SpeedTest;

    public DashboardViewModel Dashboard { get; }
    public LiveMonitoringViewModel LiveMonitoring { get; }
    public EventViewerViewModel EventViewer { get; }
    public SpeedTestViewModel SpeedTest { get; }

    public MainWindowViewModel(
        DashboardViewModel dashboard,
        LiveMonitoringViewModel liveMonitoring,
        EventViewerViewModel eventViewer,
        SpeedTestViewModel speedTest)
    {
        Dashboard = dashboard;
        LiveMonitoring = liveMonitoring;
        EventViewer = eventViewer;
        SpeedTest = speedTest;
    }

    partial void OnActiveTabChanged(AppTab value)
    {
        OnPropertyChanged(nameof(IsDashboardSelected));
        OnPropertyChanged(nameof(IsLiveMonitoringSelected));
        OnPropertyChanged(nameof(IsEventViewerSelected));
        OnPropertyChanged(nameof(IsSpeedTestSelected));
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        ((App)System.Windows.Application.Current).ApplyTheme(IsDarkTheme);
    }

    [RelayCommand]
    private void SelectDashboard() => ActiveTab = AppTab.Dashboard;

    [RelayCommand]
    private void SelectLiveMonitoring() => ActiveTab = AppTab.LiveMonitoring;

    [RelayCommand]
    private void SelectEventViewer() => ActiveTab = AppTab.EventViewer;

    [RelayCommand]
    private void SelectSpeedTest() => ActiveTab = AppTab.SpeedTest;
}
