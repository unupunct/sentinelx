using SentinelX.Core.Interfaces;
using SentinelX.Core.Models;

namespace SentinelX.Infrastructure.Scoring;

/// <summary>
/// Computes the overall 0-100 stability score as a weighted average over the phase 1 category
/// set, renormalized over whichever categories currently have a registered engine. This means
/// registering new engines later (hardware, drivers, ETW/minidump, etc.) never requires
/// touching the weighting logic.
/// </summary>
public sealed class ScoringService : IScoringService
{
    private static readonly IReadOnlyDictionary<EngineCategory, double> Weights =
        new Dictionary<EngineCategory, double>
        {
            [EngineCategory.Obs] = 0.45,
            [EngineCategory.StreamValidation] = 0.35,
            [EngineCategory.SplitCam] = 0.20
        };

    public int ComputeOverallScore(IReadOnlyDictionary<EngineCategory, int> categoryScores)
    {
        if (categoryScores.Count == 0)
        {
            return 100;
        }

        double weightedSum = 0;
        double weightTotal = 0;

        foreach (var (category, score) in categoryScores)
        {
            var weight = Weights.TryGetValue(category, out var w) ? w : 0;
            weightedSum += weight * score;
            weightTotal += weight;
        }

        if (weightTotal <= 0)
        {
            return (int)Math.Round(categoryScores.Values.Average());
        }

        return (int)Math.Round(weightedSum / weightTotal);
    }
}
