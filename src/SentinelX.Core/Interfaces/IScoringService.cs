using SentinelX.Core.Models;

namespace SentinelX.Core.Interfaces;

/// <summary>
/// Combines individual engine health scores into a single overall stability score (0-100).
/// </summary>
public interface IScoringService
{
    int ComputeOverallScore(IReadOnlyDictionary<EngineCategory, int> categoryScores);
}
