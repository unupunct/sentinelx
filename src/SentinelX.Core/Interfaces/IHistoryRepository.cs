using SentinelX.Core.Models;

namespace SentinelX.Core.Interfaces;

/// <summary>
/// Persists scan snapshots for the historical trend view.
/// </summary>
public interface IHistoryRepository
{
    Task SaveScanAsync(ScanSnapshot snapshot, CancellationToken cancellationToken);
    Task<IReadOnlyList<ScanSnapshot>> GetRecentAsync(int count, CancellationToken cancellationToken);
}
