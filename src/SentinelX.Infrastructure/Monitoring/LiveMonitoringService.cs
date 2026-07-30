using CommunityToolkit.Mvvm.Messaging;
using SentinelX.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace SentinelX.Infrastructure.Monitoring;

/// <summary>
/// Single background loop that samples every registered <see cref="IMetricsSource"/> once per
/// tick and publishes the results via <see cref="WeakReferenceMessenger"/>. A source that
/// throws is logged and skipped for that tick — it never stops the loop or the other sources.
/// </summary>
public sealed class LiveMonitoringService : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private readonly IEnumerable<IMetricsSource> _sources;
    private readonly ILogger<LiveMonitoringService> _logger;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _loopTask;

    public LiveMonitoringService(IEnumerable<IMetricsSource> sources, ILogger<LiveMonitoringService> logger)
    {
        _sources = sources;
        _logger = logger;
    }

    public void Start()
    {
        if (_cancellationTokenSource is not null)
        {
            return;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunLoopAsync(_cancellationTokenSource.Token));
    }

    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource = null;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var source in _sources)
            {
                try
                {
                    var sample = await source.SampleAsync(cancellationToken);
                    WeakReferenceMessenger.Default.Send(new MetricSampleMessage(sample));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Metrics source {SourceId} failed to sample", source.Id);
                }
            }

            try
            {
                await Task.Delay(TickInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected on Stop().
            }
        }
    }

    public void Dispose() => Stop();
}
