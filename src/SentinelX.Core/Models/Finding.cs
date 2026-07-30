namespace SentinelX.Core.Models;

/// <summary>
/// A single diagnosed issue, always explained in plain English rather than a raw code.
/// </summary>
public sealed record Finding(
    string Title,
    string Explanation,
    Severity Severity,
    double Confidence,
    string RecommendedAction,
    string EstimatedImpact);
