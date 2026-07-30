using SentinelX.Core.Models;
using SentinelX.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace SentinelX.Infrastructure.Tests.Persistence;

public class HistoryRepositoryTests : IDisposable
{
    private readonly string _databasePath;
    private readonly HistoryRepository _repository;

    public HistoryRepositoryTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"sentinelx-tests-{Guid.NewGuid():N}.db");
        _repository = new HistoryRepository(_databasePath);
    }

    [Fact]
    public async Task SaveAndRetrieve_RoundTripsAllFields()
    {
        var finding = new Finding("Title", "Explanation", Severity.Warning, 0.8, "Action", "Impact");
        var snapshot = new ScanSnapshot(
            DateTimeOffset.Parse("2026-01-15T10:30:00-05:00"),
            77,
            new Dictionary<EngineCategory, int> { [EngineCategory.Obs] = 80, [EngineCategory.StreamValidation] = 74 },
            new List<Finding> { finding });

        await _repository.SaveScanAsync(snapshot, CancellationToken.None);
        var recent = await _repository.GetRecentAsync(10, CancellationToken.None);

        var saved = Assert.Single(recent);
        Assert.Equal(snapshot.Timestamp, saved.Timestamp);
        Assert.Equal(77, saved.OverallScore);
        Assert.Equal(80, saved.CategoryScores[EngineCategory.Obs]);
        Assert.Equal(74, saved.CategoryScores[EngineCategory.StreamValidation]);
        var savedFinding = Assert.Single(saved.Findings);
        Assert.Equal(finding, savedFinding);
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsNewestFirst()
    {
        await _repository.SaveScanAsync(MakeSnapshot(1), CancellationToken.None);
        await _repository.SaveScanAsync(MakeSnapshot(2), CancellationToken.None);
        await _repository.SaveScanAsync(MakeSnapshot(3), CancellationToken.None);

        var recent = await _repository.GetRecentAsync(10, CancellationToken.None);

        Assert.Equal(new[] { 3, 2, 1 }, recent.Select(s => s.OverallScore));
    }

    [Fact]
    public async Task GetRecentAsync_RespectsCountLimit()
    {
        for (var i = 0; i < 5; i++)
        {
            await _repository.SaveScanAsync(MakeSnapshot(i), CancellationToken.None);
        }

        var recent = await _repository.GetRecentAsync(2, CancellationToken.None);

        Assert.Equal(2, recent.Count);
    }

    [Fact]
    public async Task GetRecentAsync_OnEmptyDatabase_ReturnsEmptyList()
    {
        var recent = await _repository.GetRecentAsync(10, CancellationToken.None);

        Assert.Empty(recent);
    }

    private static ScanSnapshot MakeSnapshot(int overallScore) => new(
        DateTimeOffset.Now,
        overallScore,
        new Dictionary<EngineCategory, int> { [EngineCategory.Obs] = overallScore },
        new List<Finding>());

    public void Dispose()
    {
        // Microsoft.Data.Sqlite pools connections by default, keeping a file handle open
        // after the last SqliteConnection is disposed — clear the pool before deleting.
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
