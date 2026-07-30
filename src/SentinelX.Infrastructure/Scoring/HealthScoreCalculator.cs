using SentinelX.Core.Interfaces;
using SentinelX.Core.Models;

namespace SentinelX.Infrastructure.Scoring;

/// <summary>
/// Starts every engine at a perfect 100 and deducts a severity-scaled amount per finding,
/// floored at 0. Shared by every engine so scoring never has to be reimplemented per engine.
/// </summary>
public sealed class HealthScoreCalculator : IHealthScoreCalculator
{
    private const int CriticalDeduction = 40;
    private const int WarningDeduction = 15;
    private const int InfoDeduction = 0;

    public int FromFindings(IReadOnlyList<Finding> findings)
    {
        var score = 100;
        foreach (var finding in findings)
        {
            score -= finding.Severity switch
            {
                Severity.Critical => CriticalDeduction,
                Severity.Warning => WarningDeduction,
                Severity.Info => InfoDeduction,
                _ => 0
            };
        }

        return Math.Max(0, score);
    }
}
