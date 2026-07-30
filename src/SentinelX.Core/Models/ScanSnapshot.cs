namespace SentinelX.Core.Models;

/// <summary>
/// A persisted point-in-time scan result, used to render the historical trend panel.
/// </summary>
public sealed record ScanSnapshot(
    DateTimeOffset Timestamp,
    int OverallScore,
    IReadOnlyDictionary<EngineCategory, int> CategoryScores,
    IReadOnlyList<Finding> Findings);
