using SentinelX.Core.Models;

namespace SentinelX.Core.Interfaces;

/// <summary>
/// A subsystem analyzer that performs a deeper, on-demand health assessment (as opposed to
/// cheap continuous sampling — see <see cref="IMetricsSource"/>). Implementations must never
/// throw; unexpected failures should be captured in <see cref="EngineResult.Error"/>.
/// </summary>
public interface IDiagnosticEngine
{
    string Id { get; }
    string DisplayName { get; }
    EngineCategory Category { get; }

    Task<EngineResult> RunAsync(CancellationToken cancellationToken);
}
