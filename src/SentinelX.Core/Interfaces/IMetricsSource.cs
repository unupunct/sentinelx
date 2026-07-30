using SentinelX.Core.Models;

namespace SentinelX.Core.Interfaces;

/// <summary>
/// Optional companion to <see cref="IDiagnosticEngine"/> for subsystems that also produce
/// cheap, continuously-sampled live telemetry (e.g. OBS encoder/dropped-frame stats). Sampled
/// on a timer by whatever live-monitoring service is registered; a single failed sample must
/// not throw out of the sampling loop.
/// </summary>
public interface IMetricsSource
{
    string Id { get; }
    TimeSpan SamplingInterval { get; }

    Task<MetricSample> SampleAsync(CancellationToken cancellationToken);
}
