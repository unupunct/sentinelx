using System.Text.Json;
using SentinelX.Core.Interfaces;
using SentinelX.Core.Models;
using Microsoft.Data.Sqlite;

namespace SentinelX.Infrastructure.Persistence;

/// <summary>
/// Stores scan snapshots directly via <see cref="Microsoft.Data.Sqlite"/> (no EF Core, to keep
/// startup and memory overhead low). The database file is written next to the executable so
/// history travels with a portable/USB install; if that directory isn't writable it falls
/// back to the per-user local app data folder. Per-metric time-series history (e.g. day-over-day
/// OBS dropped-frame trends) is out of scope for phase 1 — only whole-scan snapshots are stored.
/// </summary>
public sealed class HistoryRepository : IHistoryRepository
{
    private readonly string _connectionString;

    public HistoryRepository()
        : this(ResolveDatabasePath())
    {
    }

    internal HistoryRepository(string databasePath)
    {
        _connectionString = $"Data Source={databasePath}";
        InitializeSchema();
    }

    public async Task SaveScanAsync(ScanSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Scans (Timestamp, OverallScore, CategoryScoresJson, FindingsJson)
            VALUES ($timestamp, $overallScore, $categoryScoresJson, $findingsJson);
            """;
        command.Parameters.AddWithValue("$timestamp", snapshot.Timestamp.ToString("O"));
        command.Parameters.AddWithValue("$overallScore", snapshot.OverallScore);
        command.Parameters.AddWithValue("$categoryScoresJson", JsonSerializer.Serialize(snapshot.CategoryScores));
        command.Parameters.AddWithValue("$findingsJson", JsonSerializer.Serialize(snapshot.Findings));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ScanSnapshot>> GetRecentAsync(int count, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Timestamp, OverallScore, CategoryScoresJson, FindingsJson
            FROM Scans ORDER BY Id DESC LIMIT $count;
            """;
        command.Parameters.AddWithValue("$count", count);

        var results = new List<ScanSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var timestamp = DateTimeOffset.Parse(reader.GetString(0));
            var overallScore = reader.GetInt32(1);
            var categoryScores = JsonSerializer.Deserialize<Dictionary<EngineCategory, int>>(reader.GetString(2))
                                  ?? new Dictionary<EngineCategory, int>();
            var findings = JsonSerializer.Deserialize<List<Finding>>(reader.GetString(3))
                           ?? new List<Finding>();

            results.Add(new ScanSnapshot(timestamp, overallScore, categoryScores, findings));
        }

        return results;
    }

    private void InitializeSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Scans (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                OverallScore INTEGER NOT NULL,
                CategoryScoresJson TEXT NOT NULL,
                FindingsJson TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static string ResolveDatabasePath() =>
        Path.Combine(PortablePaths.ResolveWritableDirectory(), "sentinelx.db");
}
