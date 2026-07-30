using SentinelX.Core.Models;

namespace SentinelX.Core.Interfaces;

/// <summary>
/// Shared per-engine scoring rule so no engine reimplements its own deduction math.
/// </summary>
public interface IHealthScoreCalculator
{
    int FromFindings(IReadOnlyList<Finding> findings);
}
