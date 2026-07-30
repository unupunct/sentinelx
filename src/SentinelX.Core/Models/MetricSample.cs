namespace SentinelX.Core.Models;

/// <summary>
/// One tick of live telemetry from an <see cref="Interfaces.IMetricsSource"/>. Values are
/// keyed by metric name (e.g. "SkippedFramesPercent") so a single sample can carry several
/// related readings without a new type per source.
/// </summary>
public sealed record MetricSample(
    string SourceId,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, double> Values);
