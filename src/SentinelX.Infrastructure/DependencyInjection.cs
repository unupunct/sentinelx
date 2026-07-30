using SentinelX.Core.Interfaces;
using SentinelX.Infrastructure.Engines;
using SentinelX.Infrastructure.EventLog;
using SentinelX.Infrastructure.Monitoring;
using SentinelX.Infrastructure.Persistence;
using SentinelX.Infrastructure.Scoring;
using Microsoft.Extensions.DependencyInjection;

namespace SentinelX.Infrastructure;

/// <summary>
/// Composition root for everything below the WPF shell, keeping App.xaml.cs thin. New engines
/// slot in here as one more AddSingleton&lt;IDiagnosticEngine, ...&gt; (and, if they also
/// support live sampling, one more AddSingleton&lt;IMetricsSource, ...&gt; resolving the same
/// instance) — nothing else in the app needs to change.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddSentinelXInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IHealthScoreCalculator, HealthScoreCalculator>();
        services.AddSingleton<IScoringService, ScoringService>();
        services.AddSingleton<IHistoryRepository, HistoryRepository>();

        services.AddSingleton<LiveMonitorService>();
        services.AddSingleton<SystemMetricsService>();
        services.AddSingleton<EventLogService>();

        services.AddSingleton<ObsEngine>();
        services.AddSingleton<IDiagnosticEngine>(sp => sp.GetRequiredService<ObsEngine>());
        services.AddSingleton<IMetricsSource>(sp => sp.GetRequiredService<ObsEngine>());

        services.AddSingleton<IDiagnosticEngine, SplitCamEngine>();
        services.AddSingleton<IDiagnosticEngine, StreamValidationEngine>();

        services.AddSingleton<LiveMonitoringService>();

        return services;
    }
}
