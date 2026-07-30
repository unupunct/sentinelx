namespace SentinelX.Core.Models;

/// <summary>
/// The outcome of running one <see cref="Interfaces.IDiagnosticEngine"/>. Never thrown for
/// engine-internal failures — a failed collection is reported via <see cref="Error"/> instead,
/// so one broken engine never aborts a scan of the others.
/// </summary>
public sealed record EngineResult(
    EngineCategory Category,
    int HealthScore,
    IReadOnlyList<Finding> Findings,
    DateTimeOffset CollectedAt,
    TimeSpan Duration,
    string? Error = null);
